using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Conditions;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

/// A three-way answer for a yes/no fact the writer may leave unset.
public enum TriState { NotSet, Yes, No }

/// What the writer assumes about a quest; <see cref="NotSet"/> leaves it unknown.
public enum QuestChoice { NotSet, NotStarted, Active, Completed, Failed }

/// <summary>
/// The "Game State" panel's model: one row per fact the open conversation tests, each
/// editing the shared <see cref="GameState"/> directly. The state outlives any one
/// conversation (globals are game-wide), so rows are rebuilt from it on every
/// <see cref="Load"/>; values for facts the new conversation does not test are kept.
/// </summary>
public sealed partial class GameStateViewModel : ObservableObject
{
    private readonly Func<string, string, string?> _nameLookup;
    private IReadOnlyList<IReadOnlyList<ConditionNode>> _lists = [];
    private string _gameId = string.Empty;

    /// <param name="nameLookup">(lookup kind, id) to display name, or null if unknown. Kinds used:
    /// "Speaker" (companions), "Quest", "Conversation".</param>
    public GameStateViewModel(GameState state, Func<string, string, string?> nameLookup)
    {
        State = state;
        _nameLookup = nameLookup;
        State.Changed += (_, _) => RefreshSummary();
        _summary = BuildSummary();
    }

    public GameState State { get; }

    public ObservableCollection<GlobalRowViewModel> Globals { get; } = [];
    public ObservableCollection<CompanionRowViewModel> Companions { get; } = [];
    public ObservableCollection<PlayedNodeRowViewModel> PlayedNodes { get; } = [];
    public ObservableCollection<QuestRowViewModel> Quests { get; } = [];

    public bool HasGlobals => Globals.Count > 0;
    public bool HasCompanions => Companions.Count > 0;
    public bool HasPlayedNodes => PlayedNodes.Count > 0;
    public bool HasQuests => Quests.Count > 0;

    /// True when the open conversation tests IsInCombat.
    [ObservableProperty] private bool _testsCombat;

    /// "No companions in the party" (makes IsAnyCompanionActiveInParty false).
    public bool NoCompanionsInParty
    {
        get => State.NoCompanionsInParty;
        set
        {
            if (State.NoCompanionsInParty == value) return;
            State.NoCompanionsInParty = value;
            State.NotifyChanged();
            OnPropertyChanged();
        }
    }

    private TriState _combatChoice;
    public TriState CombatChoice
    {
        get => _combatChoice;
        set
        {
            if (!SetProperty(ref _combatChoice, value)) return;
            State.InCombat = value switch { TriState.Yes => true, TriState.No => false, _ => null };
            State.NotifyChanged();
        }
    }

    [ObservableProperty] private string _summary;

    /// How many of the open conversation's non-empty condition lists pass / fail / can't be told.
    public int PassCount { get; private set; }
    public int FailCount { get; private set; }
    public int UnknownCount { get; private set; }

    /// <summary>
    /// Rebuilds the rows from the conditions of the open conversation. Each entry of
    /// <paramref name="conditionLists"/> is one node's or link's condition list; empty ones
    /// are ignored. They feed both the rows (facts tested) and the summary line.
    /// </summary>
    public void Load(IReadOnlyList<IReadOnlyList<ConditionNode>> conditionLists, string gameId)
    {
        _lists = conditionLists.Where(l => l.Count > 0).ToList();
        _gameId = gameId;

        var facts = ConditionFacts.Collect(_lists.SelectMany(l => l));
        Globals.Clear(); Companions.Clear(); PlayedNodes.Clear(); Quests.Clear();
        var combat = false;
        foreach (var f in facts)
        {
            switch (f.Kind)
            {
                case FactKind.Global:
                    Globals.Add(new GlobalRowViewModel(State, f.Key));
                    break;
                case FactKind.Companion:
                    Companions.Add(new CompanionRowViewModel(State, f.Key, Name("Speaker", f.Key)));
                    break;
                case FactKind.PlayedNode:
                    PlayedNodes.Add(new PlayedNodeRowViewModel(State, f.Key, f.NodeId, Name("Conversation", f.Key)));
                    break;
                case FactKind.Quest:
                    Quests.Add(new QuestRowViewModel(State, f.Key, Name("Quest", f.Key)));
                    break;
                case FactKind.InCombat:
                    combat = true;
                    break;
            }
        }
        TestsCombat = combat;
        _combatChoice = State.InCombat switch { true => TriState.Yes, false => TriState.No, _ => TriState.NotSet };
        OnPropertyChanged(nameof(CombatChoice));
        OnPropertyChanged(nameof(NoCompanionsInParty));
        OnPropertyChanged(nameof(HasGlobals));
        OnPropertyChanged(nameof(HasCompanions));
        OnPropertyChanged(nameof(HasPlayedNodes));
        OnPropertyChanged(nameof(HasQuests));
        RefreshSummary();
    }

    private string Name(string kind, string id) => _nameLookup(kind, id) is { Length: > 0 } n ? n : id;

    [RelayCommand]
    private void ClearAll()
    {
        State.Clear();   // raises Changed, which refreshes the summary
        foreach (var r in Globals) r.Reload();
        foreach (var r in Companions) r.Reload();
        foreach (var r in PlayedNodes) r.Reload();
        foreach (var r in Quests) r.Reload();
        _combatChoice = TriState.NotSet;
        OnPropertyChanged(nameof(CombatChoice));
        OnPropertyChanged(nameof(NoCompanionsInParty));
    }

    private void RefreshSummary() => Summary = BuildSummary();

    private string BuildSummary()
    {
        int pass = 0, fail = 0, unknown = 0;
        foreach (var list in _lists)
            switch (ConditionEvaluator.Evaluate(list, State, _gameId))
            {
                case Truth.True: pass++; break;
                case Truth.False: fail++; break;
                default: unknown++; break;
            }
        PassCount = pass; FailCount = fail; UnknownCount = unknown;
        if (_lists.Count == 0) return Loc.Get("GameState_SummaryNone");
        return Loc.Format("GameState_Summary",
            Loc.FormatCount("GameState_SummaryPass", pass),
            Loc.FormatCount("GameState_SummaryFail", fail),
            Loc.FormatCount("GameState_SummaryUnknown", unknown));
    }
}

/// Base for one editable fact: edits go straight into the shared state.
public abstract class GameStateRowViewModel : ObservableObject
{
    protected GameStateRowViewModel(GameState state) { State = state; }
    protected GameState State { get; }

    /// Re-reads the row from the state (after "Clear all").
    public abstract void Reload();

    protected void Changed() => State.NotifyChanged();
}

public sealed class GlobalRowViewModel : GameStateRowViewModel
{
    private string _valueText;

    public GlobalRowViewModel(GameState state, string name) : base(state)
    {
        Name = name;
        _valueText = Read();
    }

    public string Name { get; }

    private string Read() => State.Globals.TryGetValue(Name, out var v) ? v.ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// Empty or non-numeric text means "not set": the fact is removed, nothing throws.
    public string ValueText
    {
        get => _valueText;
        set
        {
            if (!SetProperty(ref _valueText, value ?? string.Empty)) return;
            if (int.TryParse(_valueText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                State.Globals[Name] = n;
            else
                State.Globals.Remove(Name);
            Changed();
        }
    }

    public override void Reload() { _valueText = Read(); OnPropertyChanged(nameof(ValueText)); }
}

public sealed class CompanionRowViewModel : GameStateRowViewModel
{
    private TriState _choice;

    public CompanionRowViewModel(GameState state, string guid, string displayName) : base(state)
    {
        Guid = guid; DisplayName = displayName; _choice = Read();
    }

    public string Guid { get; }
    public string DisplayName { get; }

    private TriState Read() => State.CompanionsInParty.TryGetValue(Guid, out var b) ? (b ? TriState.Yes : TriState.No) : TriState.NotSet;

    /// Yes = in the party, No = not in the party.
    public TriState Choice
    {
        get => _choice;
        set
        {
            if (!SetProperty(ref _choice, value)) return;
            if (value == TriState.NotSet) State.CompanionsInParty.Remove(Guid);
            else State.CompanionsInParty[Guid] = value == TriState.Yes;
            Changed();
        }
    }

    public override void Reload() { _choice = Read(); OnPropertyChanged(nameof(Choice)); }
}

public sealed class PlayedNodeRowViewModel : GameStateRowViewModel
{
    private TriState _choice;

    public PlayedNodeRowViewModel(GameState state, string conversation, int nodeId, string conversationDisplayName) : base(state)
    {
        Conversation = conversation; NodeId = nodeId; ConversationDisplayName = conversationDisplayName; _choice = Read();
    }

    public string Conversation { get; }
    public int NodeId { get; }
    public string ConversationDisplayName { get; }

    /// What the row shows: the conversation's display name and the node's id.
    public string Label => Loc.Format("GameState_PlayedNode_Label", ConversationDisplayName, NodeId);

    private TriState Read() => State.PlayedNodes.TryGetValue((Conversation, NodeId), out var b) ? (b ? TriState.Yes : TriState.No) : TriState.NotSet;

    /// Yes = the node has been played, No = it has not.
    public TriState Choice
    {
        get => _choice;
        set
        {
            if (!SetProperty(ref _choice, value)) return;
            if (value == TriState.NotSet) State.PlayedNodes.Remove((Conversation, NodeId));
            else State.PlayedNodes[(Conversation, NodeId)] = value == TriState.Yes;
            Changed();
        }
    }

    public override void Reload() { _choice = Read(); OnPropertyChanged(nameof(Choice)); }
}

public sealed class QuestRowViewModel : GameStateRowViewModel
{
    private QuestChoice _choice;

    public QuestRowViewModel(GameState state, string questId, string displayName) : base(state)
    {
        QuestId = questId; DisplayName = displayName; _choice = Read();
    }

    public string QuestId { get; }
    public string DisplayName { get; }

    private QuestChoice Read() => State.Quests.TryGetValue(QuestId, out var s)
        ? s switch
        {
            QuestStatus.NotStarted => QuestChoice.NotStarted,
            QuestStatus.Active => QuestChoice.Active,
            QuestStatus.Completed => QuestChoice.Completed,
            _ => QuestChoice.Failed,
        }
        : QuestChoice.NotSet;

    public QuestChoice Choice
    {
        get => _choice;
        set
        {
            if (!SetProperty(ref _choice, value)) return;
            switch (value)
            {
                case QuestChoice.NotSet: State.Quests.Remove(QuestId); break;
                case QuestChoice.NotStarted: State.Quests[QuestId] = QuestStatus.NotStarted; break;
                case QuestChoice.Active: State.Quests[QuestId] = QuestStatus.Active; break;
                case QuestChoice.Completed: State.Quests[QuestId] = QuestStatus.Completed; break;
                default: State.Quests[QuestId] = QuestStatus.Failed; break;
            }
            Changed();
        }
    }

    public override void Reload() { _choice = Read(); OnPropertyChanged(nameof(Choice)); }
}
