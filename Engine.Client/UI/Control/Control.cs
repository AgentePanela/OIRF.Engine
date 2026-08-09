using System;
using System.Collections.Generic;

namespace Engine.Client.UI;

/// <summary>
/// The basic node in the GUI system.
/// </summary>
public abstract partial class Control : IDisposable
{
    private bool _visible = true;

    /// <summary>
    /// Whether this control itself wants to be visible. Does not account for ancestors
    /// being invisible - use <see cref="EffectivelyVisible"/> for that.
    /// </summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value)
                return;

            _visible = value;
            NotifyEffectiveVisibilityChanged();
        }
    }

    /// <summary>
    /// Whether this control is actually visible on screen, taking every ancestor into account.
    /// </summary>
    public bool EffectivelyVisible => _visible && (Parent?.EffectivelyVisible ?? true);

    public event Action<Control>? OnVisibilityChanged;

    /// <summary>
    /// The name of this control.
    /// Names must be unique between the siblings of the control.
    /// </summary>
    public string? Name { get; set; }

    #region Hierarchy 

    private readonly List<Control> _children = new();
    public IReadOnlyList<Control> Children => _children;

    /// <summary>
    /// Our parent inside the control tree.
    /// </summary>
    /// <remarks>
    /// This cannot be changed directly. Use <see cref="AddChild" /> and such on the parent to change it.
    /// </remarks>
    public Control? Parent { get; private set; }

    public T? FindControl<T>(string name) where T : Control
    {
        foreach (var child in Children)
        {
            if (child.Name == name && child is T typed)
                return typed;

            if (child.FindControl<T>(name) is { } found)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Adds a control as a child of this one. The child must not already have a parent.
    /// </summary>
    public void AddChild(Control child)
    {
        if (child == this)
            throw new InvalidOperationException("A control cannot be its own child.");

        if (child.Parent == this)
            return;

        if (child.Parent is not null)
            throw new InvalidOperationException("Control already has a parent. Remove it from its current parent first.");

        for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor == child)
                throw new InvalidOperationException("Cannot add an ancestor of this control as one of its children.");
        }

        _children.Add(child);
        child.Parent = this;

        if (!child.EffectivelyVisible || !EffectivelyVisible)
            child.NotifyEffectiveVisibilityChanged();
    }

    /// <summary>
    /// Removes a child from this control. Does nothing if the child is not parent of this control.
    /// </summary>
    public void RemoveChild(Control child, bool dispose = false)
    {
        if (child.Parent != this)
            return;

        _children.Remove(child);
        child.Parent = null;
        child.NotifyEffectiveVisibilityChanged();
        if (dispose)
            child.Dispose();
    }

    #endregion

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        OnDispose();
        
        foreach (var child in _children.ToArray())
            child.Dispose();

        Parent?.RemoveChild(this);
    }

    /// <summary>
    /// Happends right before all dispose logic.
    /// </summary>
    protected virtual void OnDispose()
    {
        
    }

    /// <summary>
    /// Called when this control's effective visibility in the control tree changed -
    /// either its own <see cref="Visible"/> flag flipped, or an ancestor's did.
    /// </summary>
    protected virtual void VisibilityChanged(bool newEffectiveVisible)
    {
    }

    private void NotifyEffectiveVisibilityChanged()
    {
        VisibilityChanged(EffectivelyVisible);
        OnVisibilityChanged?.Invoke(this);

        foreach (var child in _children)
            child.NotifyEffectiveVisibilityChanged();
    }
}
