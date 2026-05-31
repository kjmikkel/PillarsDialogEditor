using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public record EndpointOption(string Label, DiffEndpoint Endpoint);

public partial class DiffViewModel : ObservableObject
{
    private readonly IGitRunner          _git;
    private readonly IDispatcher         _dispatcher;
    private readonly string              _projectFilePath;
    private readonly IGameDataProvider?  _provider;
    private readonly string              _language;
    private readonly ProjectVersionLoader _loader;

    private DialogProject? _leftProject;
    private DialogProject? _rightProject;

    public IReadOnlyList<EndpointOption>           EndpointOptions { get; }
    public ObservableCollection<ConversationChange> Changes         { get; } = [];
    public ObservableCollection<ConversationChangeViewModel> Groups  { get; } = [];

    [ObservableProperty] private EndpointOption?      _leftEndpoint;
    [ObservableProperty] private EndpointOption?      _rightEndpoint;
    [ObservableProperty] private ConversationChange?  _selected;
    [ObservableProperty] private string               _statusText  = "";
    [ObservableProperty] private ConversationViewModel? _diffCanvas;
    [ObservableProperty] private string               _canvasHint  = "";
    [ObservableProperty] private CanvasMode           _canvasMode  = CanvasMode.Changes;
    [ObservableProperty] private ConversationChangeViewModel? _selectedGroup;

    // True when exactly one endpoint is the working copy (the writable target).
    private bool WorkingCopyIsEndpoint =>
        (LeftEndpoint?.Endpoint is DiffEndpoint.WorkingCopy) ^
        (RightEndpoint?.Endpoint is DiffEndpoint.WorkingCopy);

    private DialogProject? TargetProject =>
        LeftEndpoint?.Endpoint is DiffEndpoint.WorkingCopy ? _leftProject : _rightProject;
    private DialogProject? SourceProject =>
        LeftEndpoint?.Endpoint is DiffEndpoint.WorkingCopy ? _rightProject : _leftProject;

    public bool CanApply =>
        WorkingCopyIsEndpoint && TargetProject is not null && SourceProject is not null
        && Groups.Any(g => g.SelectedNodeIds.Count > 0);

    /// <summary>Raised when the user brings in changes; the host persists the new project.</summary>
    public Action<DialogProject>? CommitApply { get; set; }

    /// <summary>Raised when the user clicks "Undo bring-in"; the host reverses the last apply.</summary>
    public Action? RequestUndoApply { get; set; }

    public ObservableCollection<DanglingLink> DanglingLinks { get; } = [];

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        if (TargetProject is null || SourceProject is null) return;

        var selection = Groups
            .SelectMany(g => g.SelectedNodeIds.Select(id => new NodeSelection(g.Name, id)))
            .ToList();
        if (selection.Count == 0) return;

        DialogProject result;
        try
        {
            result = NodeApplyBuilder.Apply(TargetProject, SourceProject, selection);
        }
        catch (Exception ex)
        {
            AppLog.Error("DiffViewModel: bring-in failed", ex);
            StatusText = Loc.Format("Status_BringInError", ex.Message);
            return;
        }

        DanglingLinks.Clear();
        foreach (var d in NodeLinkAnalyzer.Analyze(result))
            DanglingLinks.Add(d);

        CommitApply?.Invoke(result);
        StatusText = Loc.Format("Status_BroughtIn", selection.Count);
    }

    public DiffViewModel(
        IGitRunner        git,
        IDispatcher       dispatcher,
        string            projectFilePath,
        IGameDataProvider? provider = null,
        string            language  = "en")
    {
        _git             = git;
        _dispatcher      = dispatcher;
        _projectFilePath = projectFilePath;
        _provider        = provider;
        _language        = language;
        _loader          = new ProjectVersionLoader(git);

        EndpointOptions = BuildEndpointOptions();

        var workingCopyOption = EndpointOptions.First(o => o.Endpoint is DiffEndpoint.WorkingCopy);
        // Your copy on the left (the bring-in target); the other version on the right.
        // This makes the left→right diff direction match what "Bring in" does.
        LeftEndpoint  = workingCopyOption;
        RightEndpoint = EndpointOptions.FirstOrDefault(o => o.Endpoint is DiffEndpoint.GitRef)
                        ?? workingCopyOption;

        Recompute();
    }

    // ── partial callbacks from [ObservableProperty] ───────────────────────

    partial void OnLeftEndpointChanged(EndpointOption? value)  => Recompute();
    partial void OnRightEndpointChanged(EndpointOption? value) => Recompute();

    partial void OnSelectedChanged(ConversationChange? value) => BuildDiffCanvas();

    partial void OnSelectedGroupChanged(ConversationChangeViewModel? value)
        => Selected = value is null ? null : Changes.FirstOrDefault(c => c.Name == value.Name);

    // ── private ───────────────────────────────────────────────────────────

    private IReadOnlyList<EndpointOption> BuildEndpointOptions()
    {
        var options = new List<EndpointOption>
        {
            new(Loc.Get("Diff_WorkingCopy"), new DiffEndpoint.WorkingCopy()),
        };

        var dir = Path.GetDirectoryName(Path.GetFullPath(_projectFilePath));
        if (dir is null) return options;

        try
        {
            // Branches
            var branchResult = _git.Run(dir, "branch", "--format=%(refname:short)");
            if (branchResult.Ok)
            {
                foreach (var raw in branchResult.StdOut.Split('\n'))
                {
                    var name = raw.Trim();
                    if (name.Length > 0)
                        options.Add(new EndpointOption(name, new DiffEndpoint.GitRef(name)));
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"DiffViewModel: failed to list git branches: {ex.Message}");
        }

        try
        {
            // Recent commits
            var logResult = _git.Run(dir, "log", "-n", "20", "--format=%h %s");
            if (logResult.Ok)
            {
                foreach (var raw in logResult.StdOut.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;
                    var sha = line.Split(' ')[0];
                    options.Add(new EndpointOption(line, new DiffEndpoint.GitRef(sha)));
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"DiffViewModel: failed to list git log: {ex.Message}");
        }

        return options;
    }

    private void Recompute()
    {
        Changes.Clear();
        Groups.Clear();
        _leftProject  = null;
        _rightProject = null;

        if (LeftEndpoint is null || RightEndpoint is null)
            return;

        try
        {
            _leftProject  = _loader.Load(LeftEndpoint.Endpoint,  _projectFilePath);
            _rightProject = _loader.Load(RightEndpoint.Endpoint, _projectFilePath);
            var results   = ProjectDiff.Diff(_leftProject, _rightProject);

            foreach (var change in results)
                Changes.Add(change);

            foreach (var change in results)
            {
                var group = new ConversationChangeViewModel(change);
                group.SelectionChanged += OnSelectionChanged;
                Groups.Add(group);
            }
            OnPropertyChanged(nameof(CanApply));

            StatusText = Loc.Format("Status_DiffComputed", Changes.Count);
        }
        catch (DiffException ex)
        {
            AppLog.Warn($"DiffViewModel: diff failed: {ex.Message}");
            StatusText = ex.Message;
        }

        // Reset canvas when endpoints change
        BuildDiffCanvas();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();
        if (CanvasMode == CanvasMode.AppliedPreview) BuildDiffCanvas();
    }

    partial void OnCanvasModeChanged(CanvasMode value) => BuildDiffCanvas();

    private void BuildDiffCanvas()
    {
        if (Selected is null)
        {
            DiffCanvas  = null;
            CanvasHint  = "";
            return;
        }

        if (_provider is null)
        {
            DiffCanvas  = null;
            CanvasHint  = Loc.Get("DiffWindow_NoGameFolder");
            return;
        }

        if (CanvasMode == CanvasMode.AppliedPreview && WorkingCopyIsEndpoint
            && TargetProject is not null && SourceProject is not null)
        {
            BuildAppliedPreviewCanvas();
            return;
        }

        try
        {
            var name = Selected.Name;

            // ── Reconstruct the RIGHT (new) conversation ──────────────────
            Conversation rightConv = ReconstructConversation(name, _rightProject, _provider);

            var vm = new ConversationViewModel(_dispatcher);
            vm.Load(rightConv);
            vm.IsEditable = false;

            // ── Tint nodes according to diff ──────────────────────────────
            var addedSet    = Selected.Added.ToHashSet();
            var modifiedSet = Selected.Modified.ToHashSet();
            var removedSet  = Selected.Removed.ToHashSet();

            foreach (var node in vm.Nodes)
            {
                if (addedSet.Contains(node.NodeId))
                    node.DiffStatus = DiffStatus.Added;
                else if (modifiedSet.Contains(node.NodeId))
                    node.DiffStatus = DiffStatus.Changed;
                else if (removedSet.Contains(node.NodeId))
                    node.DiffStatus = DiffStatus.Removed;
            }

            // ── Ghost removed nodes (from the left / old project) ─────────
            if (removedSet.Count > 0)
            {
                try
                {
                    Conversation leftConv = ReconstructConversation(name, _leftProject, _provider);
                    foreach (var leftNode in leftConv.Nodes)
                    {
                        if (!removedSet.Contains(leftNode.NodeId)) continue;
                        // Only inject if not already present (removed nodes are absent from right)
                        if (vm.Nodes.Any(n => n.NodeId == leftNode.NodeId)) continue;

                        var entry   = leftConv.Strings.Get(leftNode.NodeId);
                        var ghost   = new NodeViewModel(leftNode, entry);
                        ghost.OnSelected   = n => vm.SelectedNode = n;
                        ghost.Input.Owner  = ghost;
                        ghost.Output.Owner = ghost;
                        ghost.DiffStatus   = DiffStatus.Removed;
                        vm.Nodes.Add(ghost);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"DiffViewModel: could not inject ghost removed nodes for '{name}': {ex.Message}");
                }
            }

            DiffCanvas = vm;
            CanvasHint = "";
        }
        catch (Exception ex)
        {
            AppLog.Warn($"DiffViewModel: BuildDiffCanvas failed for '{Selected?.Name}': {ex.Message}");
            DiffCanvas  = null;
            CanvasHint  = ex.Message;
        }
    }

    private void BuildAppliedPreviewCanvas()
    {
        try
        {
            var name = Selected!.Name;
            var selection = Groups
                .Where(g => g.Name == name)
                .SelectMany(g => g.SelectedNodeIds.Select(id => new NodeSelection(name, id)))
                .ToList();

            var projected = NodeApplyBuilder.Apply(TargetProject!, SourceProject!, selection);

            // What changes in the working copy as a result (target → projected).
            var change = ProjectDiff.Diff(TargetProject!, projected)
                .FirstOrDefault(c => c.Name == name);

            Conversation conv = ReconstructConversation(name, projected, _provider!);
            var vm = new ConversationViewModel(_dispatcher);
            vm.Load(conv);
            vm.IsEditable = false;

            if (change is not null)
            {
                var addedSet   = change.Added.ToHashSet();
                var changedSet = change.Modified.ToHashSet();
                var removedSet = change.Removed.ToHashSet();
                foreach (var node in vm.Nodes)
                {
                    if (addedSet.Contains(node.NodeId))        node.DiffStatus = DiffStatus.Added;
                    else if (changedSet.Contains(node.NodeId)) node.DiffStatus = DiffStatus.Changed;
                    else if (removedSet.Contains(node.NodeId)) node.DiffStatus = DiffStatus.Removed;
                }
            }

            DiffCanvas = vm;
            CanvasHint = "";
        }
        catch (Exception ex)
        {
            AppLog.Warn($"DiffViewModel: applied-preview build failed for '{Selected?.Name}': {ex.Message}");
            DiffCanvas = null;
            CanvasHint = ex.Message;
        }
    }

    /// <summary>
    /// Reconstructs the effective Conversation for <paramref name="name"/> from
    /// <paramref name="project"/>'s patch (if present) over the game-data base, or
    /// just the base conversation if there is no patch.
    /// </summary>
    private Conversation ReconstructConversation(
        string name, DialogProject? project, IGameDataProvider provider)
    {
        var file = provider.FindConversation(name);

        if (project is not null && project.Patches.TryGetValue(name, out var patch))
        {
            if (file is not null)
            {
                // Base from disk + apply patch
                var conv     = provider.LoadConversation(file);
                var baseSnap = ConversationSnapshotBuilder.Build(conv);
                var merged   = PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true);
                var translations = patch.Translations.GetValueOrDefault(_language);
                return ConversationSnapshotBuilder.ToConversation(name, merged, translations);
            }
            else
            {
                // New conversation (no on-disk file): apply patch over empty snapshot
                var baseSnap = new ConversationEditSnapshot([]);
                var merged   = PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true);
                var translations = patch.Translations.GetValueOrDefault(_language);
                return ConversationSnapshotBuilder.ToConversation(name, merged, translations);
            }
        }
        else if (file is not null)
        {
            // No patch — just the on-disk conversation
            return provider.LoadConversation(file);
        }
        else
        {
            // No game-data file and no patch → empty
            return new Conversation(name, [], new StringTable([]));
        }
    }
}
