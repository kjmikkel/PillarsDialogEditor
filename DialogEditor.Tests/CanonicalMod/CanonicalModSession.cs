using System.Reflection;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.CanonicalMod;

/// A real editor session over a temp copy of a canonical fixture (issue 138). Every edit
/// goes through the view models the GUI uses; only the Avalonia views are skipped. The
/// private MainWindowViewModel methods called by reflection (LoadDirectory,
/// LoadProjectAsync, OnConversationSelected, SaveProject) are the GUI's own code paths,
/// the same ones the existing view-model tests drive.
public sealed class CanonicalModSession : IDisposable
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    // A space in the root on purpose: install paths with spaces are the norm on Windows.
    public string Root        { get; } = Path.Combine(Path.GetTempPath(), $"canonical mod {Guid.NewGuid():N}");
    public string GameRoot    => Path.Combine(Root, "game");
    public string ProjectPath { get; private set; } = "";
    public MainWindowViewModel Vm { get; }

    private CanonicalModSession()
    {
        Loc.Configure(new StubStringProvider());
        Directory.CreateDirectory(Root);
        AppSettings.SettingsPathOverride = Path.Combine(Root, "settings.json");
        Vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
    }

    public static async Task<CanonicalModSession> OpenAsync(string game, string projectName)
    {
        var s = new CanonicalModSession();
        CanonicalFixture.CopyTo(game, s.GameRoot);
        s.Invoke("LoadDirectory", s.GameRoot);

        s.ProjectPath = Path.Combine(s.Root, "project", projectName + ".dialogproject");
        DialogProjectSerializer.SaveToFile(s.ProjectPath, DialogProject.Empty(projectName));
        await (Task)s.Invoke("LoadProjectAsync", s.ProjectPath, false)!;
        return s;
    }

    private IGameDataProvider Provider =>
        (IGameDataProvider)typeof(MainWindowViewModel).GetField("_provider", Private)!.GetValue(Vm)!;

    public void Open(string conversation)
    {
        var file = Provider.FindConversation(conversation)
                   ?? throw new InvalidOperationException($"no conversation '{conversation}'");
        Invoke("OnConversationSelected", file);
    }

    /// Saves, then switches the text language the way the toolbar does (the conversation reloads).
    public Task SwitchLanguageAsync(string lang)
    {
        Save();
        Vm.SelectedLanguage = lang;
        return Task.CompletedTask;
    }

    public NodeViewModel Node(int id) => Vm.Canvas.Nodes.Single(n => n.NodeId == id);

    /// Adds a node at a fixed position (layouts are saved, so positions must be deterministic)
    /// and asserts the editor allocated the id the scenario expects.
    public NodeViewModel AddNode(int expectedId, bool playerChoice, SpeakerCategory category,
                                 string speaker, string listener, double x, double y)
    {
        var id = Vm.Canvas.NextNodeId();
        Assert.Equal(expectedId, id);
        var node = new NodeViewModel(
            new ConversationNode(id, playerChoice, category, speaker, listener, [], [], [], "Conversation", "None"),
            new StringEntry(id, "", ""));
        Vm.Canvas.AddNode(node, new LayoutPoint(x, y));
        return node;
    }

    public void Link(int from, int to)   => Vm.Canvas.AddConnection(Node(from).Output, Node(to).Input);
    public void Unlink(int from, int to) => Vm.Canvas.DeleteConnection(LinkOf(from, to));
    public ConnectionViewModel LinkOf(int from, int to) =>
        Vm.Canvas.Connections.Single(c => c.Source.Owner == Node(from) && c.Target.Owner == Node(to));
    public void Delete(int id) => Vm.Canvas.DeleteNode(Node(id));

    /// The GUI's Save Project path: folds the canvas into the project, writes the file.
    public void Save() => Invoke("SaveProject");

    private object? Invoke(string method, params object?[] args) =>
        typeof(MainWindowViewModel).GetMethod(method, Private)!.Invoke(Vm, args);

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { Directory.Delete(Root, true); } catch (Exception) { /* best-effort */ }
    }
}
