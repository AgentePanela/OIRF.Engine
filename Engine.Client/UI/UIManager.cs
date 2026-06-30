using AssetManagementBase;
using Engine.Client.Scenes;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Myra;
using Myra.Graphics2D.UI;
using System;

namespace Engine.Client.UI;

/// <summary>
/// Manages the game interface, adding windows, UI screens, etc...
/// </summary>
public sealed class UIManager
{
    [Dependency] private readonly SceneManager _sceneManager = default!;

    private readonly Panel _root = new();

    public Desktop Desktop { get; private set; } = default!;

    /// <summary>
    /// True when the mouse cursor is over any UI widget (window, scrollbar, button, etc... )
    /// </summary>
    public bool IsMouseOverUI => Desktop?.IsMouseOverGUI ?? false;

    /// <summary>
    /// True when the user is with the keyboard focused in a widget.
    /// </summary>
    public bool IsKeyboardFocused = false;

    public UICanvas? CurrentScreen { get; private set; }

    private UICanvas? _destScreen;

    public void Init()
    {
        IoCManager.ResolveDependencies(this);

        MyraEnvironment.Game = GameClient.Instance;

        Desktop = new Desktop
        {
            Root = _root
        };

        Resize();
        _sceneManager.OnBeforeSceneInit += SceneChanged;

        Desktop.WidgetGotKeyboardFocus += (_, _) => IsKeyboardFocused = true;
        Desktop.WidgetLosingKeyboardFocus += (_, _) => IsKeyboardFocused = false;
    }

    internal void Resize()
    {
        var width = GameClient.Graphics.PreferredBackBufferWidth;
        var height = GameClient.Graphics.PreferredBackBufferHeight;

        _root.InvalidateMeasure();
        Desktop.BoundsFetcher = () => new Rectangle(0, 0, width, height);
    }

    private void SceneChanged(Scene scene)
    {
        if (scene.DefaultCanvas is null)
        {
            RemoveCurrentScreen(true);
            return;
        }

        SetDestinationScreen(scene.DefaultCanvas);
        PerformScreenTransition();
    }

    /// <summary>
    /// Remove all the widgets from the current screen.
    /// </summary>
    public void ClearCurrentScreen()
    {
        var screenRoot = _root.FindChildById("_CanvasUI");
        if (screenRoot is not null)
            _root.Widgets.Remove(screenRoot);
    }

    /// <summary>
    /// Clears all UI widgets.
    /// </summary>
    public void ClearUi() 
        => _root.Widgets.Clear();

    /// <summary>
    /// Set the next screen to be shown in the next frame.
    /// </summary>
    public void SetDestinationScreen(UICanvas destination) 
        => _destScreen = destination;

    public void AddRootWidget(Widget widget)
    {
        if (!_root.Widgets.Contains(widget))
            _root.Widgets.Add(widget);
    }

    public void RemoveRootWidget(Widget widget) 
        => _root.Widgets.Remove(widget);

    public T? GetRootWidget<T>(string id) where T : Widget 
        => _root.FindChildById<T>(id);

    public void Update(float dt)
    {
        if (_destScreen is not null)
            PerformScreenTransition();

        CurrentScreen?.Update(dt);
    }

    public void Draw(float dt)
    {
        CurrentScreen?.Draw(dt);
        Desktop.Render();
    }

    public void RemoveCurrentScreen(bool clearInstance = false)
    {
        if (CurrentScreen is not null)
        {
            CurrentScreen.Dispose();
            ClearCurrentScreen();
        }

        if (clearInstance)
            CurrentScreen = null;
    }

    private void PerformScreenTransition()
    {
        RemoveCurrentScreen(true);

        CurrentScreen = _destScreen;
        _destScreen = null;

        if (CurrentScreen is null)
            throw new NullReferenceException("Current screen is null during a screen transition!");

        IoCManager.ResolveDependencies(CurrentScreen);
        CurrentScreen.BuildElements();
        _root.Widgets.Add(CurrentScreen.Root);
        CurrentScreen.Initialize();
    }
}
