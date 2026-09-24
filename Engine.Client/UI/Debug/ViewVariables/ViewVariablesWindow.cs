using System.Collections.Generic;
using Engine.Shared.Debug.ViewVariables;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Factories;
using Engine.Shared.IoC;

namespace Engine.Client.UI.Debug.ViewVariables;

/// <summary>
/// multi-instance, deduped by path - always opened through Open/OpenEntity
/// </summary>
public sealed partial class ViewVariablesWindow : Window
{
    private const float RefreshInterval = 0.25f;

    private static readonly Dictionary<VVPath, ViewVariablesWindow> _open = new();

    public static ViewVariablesWindow Open(VVPath path)
    {
        var windows = IoCManager.Resolve<WindowManager>();

        if (_open.TryGetValue(path, out var existing) ||
            (TryGetCounterpart(path, out var counterpart) && _open.TryGetValue(counterpart!, out existing)))
        {
            // one window covers both sides, so the counterpart is not a second window - just the other tab
            existing.SelectSide(path);
            windows.BringToFront(existing);
            return existing;
        }

        var window = new ViewVariablesWindow(path);
        window.OnClosed += _ => _open.Remove(path);
        _open[path] = window;

        windows.Open(window);
        return window;
    }

    public static ViewVariablesWindow OpenEntity(EntityUid uid) => Open(VVPath.Of(VVRoot.Entity(uid)));

    private readonly VVPath _path;

    // client first, server second. Only one when the other side does not exist, and then there are no tabs at all
    private readonly List<SideView> _sides = new(2);
    private readonly TabContainer? _tabs;
    private readonly Label _statusLabel;

    private float _refreshAccum;

    private SideView Current => _sides[_tabs?.CurrentTab ?? 0];

    private ViewVariablesWindow(VVPath path)
    {
        var manager = IoCManager.Resolve<ViewVariablesManager>();

        _path = path;

        Title = Loc.GetString("engine-vv-window-title");
        MinWidth = 520;
        MinHeight = 480;

        var root = new BoxContainer { Orientation = Orientation.Vertical, Separation = 6 };
        AddChild(root);

        root.AddChild(BuildHeader(path));

        BuildSides(manager, path);

        if (_sides.Count > 1)
        {
            _tabs = new TabContainer { VerticalExpand = true, HorizontalExpand = true };
            foreach (var side in _sides)
                _tabs.AddTab(side.Title, side.Root);

            _tabs.OnTabChanged += _ =>
            {
                ApplyTitle();
                RefreshCurrent();
            };
            root.AddChild(_tabs);
        }
        else
        {
            root.AddChild(_sides[0].Root);
        }

        _statusLabel = new Label { Text = "" };
        root.AddChild(_statusLabel);

        manager.Remote.OnRemoteError += OnRemoteError;

        foreach (var side in _sides)
            Rebuild(side);

        SelectSide(path);
    }

    private BoxContainer BuildHeader(VVPath path)
    {
        var header = new BoxContainer { Orientation = Orientation.Horizontal, Separation = 8, Background = new (0, 0, 0, 0.5f) };

        if (path.Root.Kind == VVRootKind.Entity && path.Root.Side == VVSide.Client)
            header.AddChild(new EntityView(new EntityUid(path.Root.Uid)) { MinWidth = 48, MinHeight = 48, MaxWidth = 128, MaxHeight = 128 });

        // pushes everything after it to the right edge - BoxContainer only splits leftover
        // space among HorizontalExpand children, it has no float-right of its own
        header.AddChild(new PanelContainer { HorizontalExpand = true });

        var refreshButton = new Button(Loc.GetString("engine-vv-refresh"));
        refreshButton.OnClick += _ =>
        {
            // a remote snapshot answers from a cache, so Refresh has to ask for a new one
            foreach (var side in _sides)
            {
                side.Access.Invalidate(side.Path);
                Rebuild(side);
            }
        };
        header.AddChild(refreshButton);

        if (path.Parent is { } parent)
        {
            var upButton = new Button(Loc.GetString("engine-vv-up"));
            upButton.OnClick += _ => Open(parent);
            header.AddChild(upButton);
        }

        return header;
    }

    private void BuildSides(ViewVariablesManager manager, VVPath path)
    {
        TryGetCounterpart(path, out var counterpart);

        var clientPath = path.Root.Side == VVSide.Client ? path : counterpart;
        var serverPath = path.Root.Side == VVSide.Server ? path : counterpart;

        if (clientPath is not null)
            _sides.Add(new SideView(Loc.GetString("engine-vv-tab-client"), clientPath, manager.For(clientPath.Root), this));

        if (serverPath is not null)
            _sides.Add(new SideView(Loc.GetString("engine-vv-tab-server"), serverPath, manager.For(serverPath.Root), this));
    }

    /// <summary>
    /// The same target on the other side of the wire, if it has one. Entities are matched by
    /// <see cref="NetEntity"/> and components by name.
    /// </summary>
    private static bool TryGetCounterpart(VVPath path, out VVPath? other)
    {
        other = null;

        var root = path.Root;
        if (root.Kind == VVRootKind.Detached)
            return false;

        var entMan = IoCManager.Resolve<EntityManager>();

        if (root.Side == VVSide.Client)
        {
            var netEnt = entMan.GetNetEntity(new EntityUid(root.Uid));
            if (!netEnt.IsValid)
                return false;

            other = path.WithRoot(root.Kind == VVRootKind.Component
                ? VVRoot.ServerComponent(netEnt, root.ComponentTypeName!)
                : VVRoot.ServerEntity(netEnt));

            return true;
        }

        if (!entMan.TryGetEntity(new NetEntity(root.Uid), out var uid))
            return false;

        if (root.Kind != VVRootKind.Component)
        {
            other = path.WithRoot(VVRoot.Entity(uid));
            return true;
        }

        // a component only the server has cannot be addressed locally, so that window has no client side
        var type = IoCManager.Resolve<ComponentFactory>().GetTypeByString(root.ComponentTypeName!);
        if (type is null)
            return false;

        other = path.WithRoot(VVRoot.Component(uid, type));
        return true;
    }

    private void SelectSide(VVPath path)
    {
        if (_tabs is null)
            return;

        for (var i = 0; i < _sides.Count; i++)
        {
            if (!_sides[i].Path.Equals(path))
                continue;

            _tabs.CurrentTab = i;
            return;
        }
    }

    private void ApplyTitle()
    {
        var title = Current.SnapshotTitle;
        Title = string.IsNullOrEmpty(title)
            ? Loc.GetString("engine-vv-window-title")
            : Loc.GetString("engine-vv-window-title-target", ("target", title));
    }

    private void OnRemoteError(string error) => _statusLabel.Text = error;

    protected override void OnDispose()
    {
        IoCManager.Resolve<ViewVariablesManager>().Remote.OnRemoteError -= OnRemoteError;
        base.OnDispose();
    }

    protected override void Update(float dt)
    {
        base.Update(dt);

        if (!EffectivelyVisible)
            return;

        _refreshAccum += dt;
        if (_refreshAccum < RefreshInterval)
            return;

        _refreshAccum = 0f;
        RefreshCurrent();
    }

    // only the visible side is polled - asking the server for a tab nobody is looking at is pure traffic
    private void RefreshCurrent()
    {
        var side = Current;

        // a remote snapshot arrives whenever it arrives, so rows are rebuilt off its structure changing
        if (side.Access.IsRemote && side.Access.Snapshot(side.Path).StructureVersion != side.Structure)
        {
            Rebuild(side);
            return;
        }

        foreach (var row in side.Rows)
            row.Refresh();
    }
}
