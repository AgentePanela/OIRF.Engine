using Engine.Shared.IoC;
using Engine.Client.Inputs;
using Engine.Client.UI.Debug;
using Microsoft.Xna.Framework.Input;
using Myra.Graphics2D.UI;
using System;
using System.Collections.Generic;
using System.Linq;


namespace Engine.Client.UI;

[RegisterIoC]
public sealed class WindowManager
{
    [Dependency] private readonly InputManager _input = default!;
    [Dependency] private readonly UIManager _ui = default!;

    private readonly Panel _rootWidget = new() { Id = "_WindowsDefault" };
    private readonly List<DefaultWindow> _windows = new();
    private readonly Dictionary<DefaultWindow, EventHandler> _closedHandlers = new();

    private DebugOverlayScreen? _debugOverlay;

    public WindowManager()
    {
        IoCManager.ResolveDependencies(this);
        _ui.AddRootWidget(_rootWidget);
        _rootWidget.ZIndex = 999;
    }

    public DefaultWindow OpenWindow(DefaultWindow window)
    {
        if (_windows.Contains(window))
            return window;

        window.Id = window.GetType().Name;

        IoCManager.ResolveDependencies(window);
        window.BuildElements();
        window.Initialize();

        _rootWidget.Widgets.Add(window);
        _windows.Add(window);

        // sstore handlers
        EventHandler handler = (_, __) => RemoveWindowInternally(window);
        _closedHandlers[window] = handler;
        window.Closed += handler;

        window.OnOpen();
        window.CenterOnDesktop();

        return window;
    }

    public T OpenWindow<T>() where T : DefaultWindow, new()
    {
        var existing = GetWindow<T>();
        if (existing is not null)
            return existing;

        var window = new T();
        return (T)OpenWindow(window);
    }

    public T? GetWindow<T>() where T : DefaultWindow 
        => _windows.OfType<T>().FirstOrDefault();

    public DefaultWindow? GetWindow(int index)
    {
        if (index < 0 || index >= _windows.Count)
            return null;

        return _windows[index];
    }

    public int GetWindowCount()
        => _windows.Count();

    public bool TryClose(int index, bool tryNext = false)
    {
        if (index < 0 || index >= _windows.Count)
            return false;

        var window = _windows[index];
        if (window.CloseOnEscape)
        {
            Close(window);
            return true;
        }

        if (!tryNext)
            return false;

        return TryClose(index - 1, true);
    }

    public void CloseAll()
    {        
        foreach (var window in _windows.ToArray())
            Close(window);
    }

    public void Close(DefaultWindow window)
    {
        var found = _windows.FirstOrDefault(w => w == window);
        if (found is null)
            return;

        RemoveWindowInternally(found);
        found.Close();
    }

    private void RemoveWindowInternally(DefaultWindow window)
    {
        // unsubscribe handler
        if (_closedHandlers.TryGetValue(window, out var handler))
        {
            window.Closed -= handler;
            _closedHandlers.Remove(window);
        }

        _rootWidget.Widgets.Remove(window);
        _windows.Remove(window);
        if (!window.Disposed)
            window.Dispose(); //ensure disposing
    }

    internal void Resize() 
        => _windows.ForEach(window => window.CenterOnDesktop());

    internal void Update(float dt)
    {
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            var window = _windows[i];

            float windowDt =
                GameClient.GameState != GameState.Running &&
                !window.UpdatesWhileGameplayPaused ? 0f : dt;

            window.Update(windowDt);
        }

        if (_input.KeyPressed(Keys.OemQuotes))
            OpenWindow<DebugWindow>();

        if (_input.KeyPressed(Keys.F3))
            ToggleDebugOverlay();

        if (_input.KeyDown(Keys.LeftShift) && _input.KeyPressed(Keys.Escape))
        {
            CloseAll();
            return;
        }

        if (_input.KeyPressed(Keys.Escape))
        {
            if (_windows.Count < 1)
                return;

            TryClose(_windows.Count - 1, true);
        }

        float debugDt = GameClient.GameState != GameState.Running ? 0f : dt;
        _debugOverlay?.Update(debugDt);
    }

    internal void Draw(float dt)
    {
        foreach (var window in _windows.ToArray())
            window.Draw(dt);

        _debugOverlay?.Draw(dt);
    }

    private void ToggleDebugOverlay()
    {
        if (_debugOverlay is not null)
        {
            _ui.RemoveRootWidget(_debugOverlay.Root);
            _debugOverlay.Dispose();
            _debugOverlay = null;
            return;
        }

        _debugOverlay = new DebugOverlayScreen();
        _debugOverlay.BuildElements();
        _debugOverlay.Initialize();
        _ui.AddRootWidget(_debugOverlay.Root);
    }
}
