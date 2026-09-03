using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

/// <summary>
/// In-memory IUiaTree. AuditSnapshot() reproduces the project-open surface measured on
/// 2026-09-03 — including its collisions — so resolver rules are exercised against the
/// real shape of this app's ambiguity rather than invented examples.
/// </summary>
public sealed class FakeUiaTree : IUiaTree
{
    private readonly Dictionary<string, List<ElementInfo>> _children = new();
    public ElementInfo Root { get; private set; } = null!;

    public IReadOnlyList<ElementInfo> ChildrenOf(string id) =>
        _children.TryGetValue(id, out var kids) ? kids : Array.Empty<ElementInfo>();

    private ElementInfo Add(string? parentId, string name, string controlType,
        string automationId = "", string className = "", bool enabled = true,
        bool offscreen = false, bool focusable = false, params string[] patterns)
    {
        var el = new ElementInfo($"e{_children.Count}_{Guid.NewGuid():N}", name, controlType,
            automationId, className, enabled, offscreen, focusable, patterns);
        _children[el.Id] = new List<ElementInfo>();
        if (parentId is null) Root = el;
        else _children[parentId].Add(el);
        return el;
    }

    public static FakeUiaTree AuditSnapshot()
    {
        var t = new FakeUiaTree();
        var win = t.Add(null, "Pillars Dialog Editor [AuditScratch]", "Window", className: "MainWindow");

        // Title bar: the OS system menu is a MenuItem peer of the app's own menus.
        var titleBar = t.Add(win.Id, "Pillars Dialog Editor [AuditScratch]", "TitleBar", "TitleBar");
        var sysBar = t.Add(titleBar.Id, "System Menu Bar", "MenuBar", "SystemMenuBar");
        t.Add(sysBar.Id, "System", "MenuItem", "Item 1");

        // The app's own menu is ANONYMOUS — selectable only by ClassName.
        var menu = t.Add(win.Id, "", "Menu", className: "Menu");
        foreach (var top in new[] { "File", "Edit", "View", "Test", "Help" })
        {
            var mi = t.Add(menu.Id, top, "MenuItem", className: "MenuItem");
            if (top == "File")
                foreach (var item in new[] { "New Project…", "Open Project…", "Save Project", "Close Project" })
                    t.Add(mi.Id, item, "MenuItem", className: "MenuItem");
            if (top == "Edit") { t.Add(mi.Id, "↩", "MenuItem"); t.Add(mi.Id, "↪", "MenuItem"); }
        }

        // Label and its ComboBox share a Name.
        t.Add(win.Id, "Language:", "Text", className: "TextBlock");
        t.Add(win.Id, "Language:", "ComboBox", className: "ComboBox", focusable: true,
            patterns: new[] { "ExpandCollapse", "Value" });

        var dockHost = t.Add(win.Id, "Dock host", "Pane", className: "DockControl");
        var rootDock = t.Add(dockHost.Id, "Root dock", "Pane", className: "RootDockControl");

        var left = t.Add(rootDock.Id, "Conversations", "Pane", "LeftPane", "ToolControl");
        AddPaneChrome(t, rootDock.Id);

        // 37 nameless, patternless conversation rows.
        var tree = t.Add(left.Id, "", "Tree", className: "TreeView");
        for (var i = 0; i < 37; i++)
        {
            var item = t.Add(tree.Id, "", "TreeItem", focusable: true);
            t.Add(item.Id, "", "Button", "PART_ExpandCollapseChevron");
            t.Add(item.Id, i == 0 ? "(root)" : $"{i:00}_conversation", "Text");
        }

        var docs = t.Add(rootDock.Id, "Canvas", "Pane", "Documents", "DocumentControl");
        var docTabs = t.Add(docs.Id, "Document tabs", "Tab", "Documents", "DocumentTabStrip");
        t.Add(docTabs.Id, "Canvas", "TabItem", "Canvas");

        var right = t.Add(rootDock.Id, "Node Details", "Pane", "RightPane", "ToolControl");
        AddPaneChrome(t, rootDock.Id);
        var toolTabs = t.Add(right.Id, "Tool tabs", "Tab", "RightPane", "ToolTabStrip");
        t.Add(toolTabs.Id, "Node Details", "TabItem", "Details");
        t.Add(toolTabs.Id, "Condition search", "TabItem", "ConditionSearch");

        t.Add(win.Id, "Opened project 'AuditScratch' (0 patches)", "Text", "StatusLiveRegion");
        return t;
    }

    // Three Viewbox-named buttons per ToolControl pane, ids duplicated across panes.
    private static void AddPaneChrome(FakeUiaTree t, string parentId)
    {
        foreach (var part in new[] { "PART_MenuButton", "PART_PinButton", "PART_CloseButton" })
            t.Add(parentId, "Avalonia.Controls.Viewbox", "Button", part, "Button",
                focusable: true, patterns: new[] { "Invoke" });
    }
}
