using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public record EndpointOption(string Label, DiffEndpoint Endpoint);

public partial class DiffViewModel : ObservableObject
{
    private readonly IGitRunner          _git;
    private readonly string              _projectFilePath;
    private readonly IGameDataProvider?  _provider;
    private readonly string              _language;
    private readonly ProjectVersionLoader _loader;

    public IReadOnlyList<EndpointOption>           EndpointOptions { get; }
    public ObservableCollection<ConversationChange> Changes         { get; } = [];

    [ObservableProperty] private EndpointOption?      _leftEndpoint;
    [ObservableProperty] private EndpointOption?      _rightEndpoint;
    [ObservableProperty] private ConversationChange?  _selected;
    [ObservableProperty] private string               _statusText = "";

    public DiffViewModel(
        IGitRunner        git,
        string            projectFilePath,
        IGameDataProvider? provider = null,
        string            language  = "en")
    {
        _git             = git;
        _projectFilePath = projectFilePath;
        _provider        = provider;
        _language        = language;
        _loader          = new ProjectVersionLoader(git);

        EndpointOptions = BuildEndpointOptions();

        // Defaults: left = first GitRef option (or working copy), right = working copy
        var workingCopyOption = EndpointOptions.First(o => o.Endpoint is DiffEndpoint.WorkingCopy);
        LeftEndpoint  = EndpointOptions.FirstOrDefault(o => o.Endpoint is DiffEndpoint.GitRef)
                        ?? workingCopyOption;
        RightEndpoint = workingCopyOption;

        Recompute();
    }

    // ── partial callbacks from [ObservableProperty] ───────────────────────

    partial void OnLeftEndpointChanged(EndpointOption? value)  => Recompute();
    partial void OnRightEndpointChanged(EndpointOption? value) => Recompute();

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

        if (LeftEndpoint is null || RightEndpoint is null)
            return;

        try
        {
            var a       = _loader.Load(LeftEndpoint.Endpoint,  _projectFilePath);
            var b       = _loader.Load(RightEndpoint.Endpoint, _projectFilePath);
            var results = ProjectDiff.Diff(a, b);

            foreach (var change in results)
                Changes.Add(change);

            StatusText = Loc.Format("Status_DiffComputed", Changes.Count);
        }
        catch (DiffException ex)
        {
            AppLog.Warn($"DiffViewModel: diff failed: {ex.Message}");
            StatusText = ex.Message;
        }
    }
}
