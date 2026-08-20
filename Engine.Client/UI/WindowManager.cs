using System.Collections.Generic;
using System.Linq;
using Engine.Client.Inputs;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework.Input;

namespace Engine.Client.UI;

/// <summary>
/// Opens, stacks and closes <see cref="Window"/>s. Owns the LayoutContainer layer they live in,
/// which the UIManager keeps above regular UI via its zIndex.
/// </summary>
public sealed class WindowManager
{
    [Dependency] private readonly InputManager _input = default!;
    [Dependency] private readonly UIManager _ui = default!;

    private readonly List<Window> _windows = new();

    /// <summary>
    /// Windows currently open, bottom of the stack first.
    /// </summary>
    public IReadOnlyList<Window> Windows => _windows;

    public WindowManager()
    {
        IoCManager.ResolveDependencies(this);
    }

    /// <summary>
    /// Opens a window, or brings the already-open one of this type to the front.
    /// </summary>
    public T Open<T>() where T : Window, new()
        => GetWindow<T>() is { } existing ? (T)BringToFront(existing) : (T)Open(new T());

    public Window Open(Window window)
    {
        if (_windows.Contains(window))
            return BringToFront(window);

        // windows ported from the old system lean on [Dependency] fields heavily
        IoCManager.ResolveDependencies(window);

        window.Manager = this;
        _windows.Add(window);
        _ui.WindowRoot.AddChild(window);
        LayoutContainer.SetAnchorPreset(window, LayoutPreset.Center);

        return window;
    }

    public T? GetWindow<T>() where T : Window => _windows.OfType<T>().FirstOrDefault();

    public Window BringToFront(Window window)
    {
        if (_windows.Remove(window))
            _windows.Add(window);

        _ui.WindowRoot.MoveChildToFront(window);
        return window;
    }

    public void Close(Window window)
    {
        if (!_windows.Remove(window))
            return;

        window.Manager = null;
        _ui.WindowRoot.RemoveChild(window);
        window.NotifyClosed();
        window.Dispose();
    }

    public void CloseAll()
    {
        foreach (var window in _windows.ToArray())
            Close(window);
    }

    /// <summary>
    /// Closes the topmost window that allows it, walking down the stack past the ones that don't.
    /// </summary>
    public bool TryCloseTopmost()
    {
        for (var i = _windows.Count - 1; i >= 0; i--)
        {
            if (!_windows[i].CloseOnEscape)
                continue;

            Close(_windows[i]);
            return true;
        }

        return false;
    }

    internal void Update(float dt)
    {
        // Control.KeyDown only reaches whoever holds keyboard focus, so a global shortcut like
        // this has to poll the InputManager - same as the old Myra WindowManager did.
        if (!_input.KeyPressed(Keys.Escape))
            return;

        if (_input.KeyDown(Keys.LeftShift) || _input.KeyDown(Keys.RightShift))
        {
            CloseAll();
            return;
        }

        TryCloseTopmost();
    }
}
