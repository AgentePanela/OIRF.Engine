using System.Collections.Generic;
using Engine.Shared.Debug.ViewVariables;
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

        if (_open.TryGetValue(path, out var existing))
        {
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
    private readonly IViewVariablesAccess _access;

    private readonly BoxContainer _body;
    private readonly Label _componentsLabel;
    private readonly ScrollContainer _componentsScroll;
    private readonly BoxContainer _componentsBody;
    private readonly Label _statusLabel;
    private readonly List<ViewVariablesRow> _rows = new();

    private float _refreshAccum;

    private ViewVariablesWindow(VVPath path)
    {
        _path = path;
        _access = IoCManager.Resolve<ViewVariablesManager>().For(path.Root);

        Title = "View Variables";
        MinWidth = 520;
        MinHeight = 480;

        var root = new BoxContainer { Orientation = Orientation.Vertical, Separation = 6 };
        AddChild(root);

        var header = new BoxContainer { Orientation = Orientation.Horizontal, Separation = 8, Background = new (0, 0, 0, 0.5f) };
        if (path.Root.Kind == VVRootKind.Entity)
            header.AddChild(new EntityView(new EntityUid(path.Root.Uid)) { MinWidth = 48, MinHeight = 48, MaxWidth = 128, MaxHeight = 128 });

        var refreshButton = new Button("Refresh") { HorizontalAlignment = HorizontalAlignment.Right};
        refreshButton.OnClick += _ => Rebuild();
        header.AddChild(refreshButton);

        if (path.Parent is { } parent)
        {
            var upButton = new Button("Up");
            upButton.OnClick += _ => Open(parent);
            header.AddChild(upButton);
        }

        root.AddChild(header);

        root.AddChild(new Label { Text = path.ToString(), AutoWrap = false });

        // var scroll = new ScrollContainer { VerticalExpand = false, HorizontalExpand = true, MinHeight = 200 };
        // root.AddChild(scroll);

        _body = new BoxContainer { Orientation = Orientation.Vertical, Separation = 4 };
        root.AddChild(_body);

        _componentsLabel = new Label { Text = "Components:", Visible = false };
        root.AddChild(_componentsLabel);

        _componentsScroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, MinHeight = 120, Visible = false };
        root.AddChild(_componentsScroll);

        _componentsBody = new BoxContainer { Orientation = Orientation.Vertical, Separation = 4, Background = new (0, 0, 0, 0.5f) };
        _componentsScroll.AddChild(_componentsBody);

        _statusLabel = new Label { Text = "" };
        root.AddChild(_statusLabel);

        Rebuild();
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
        RefreshValues();
    }
}