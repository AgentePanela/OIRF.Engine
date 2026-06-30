using Engine.Shared.IoC;
using Myra.Graphics2D.UI;
using System;

namespace Engine.Client.UI;

/// <summary>
/// Represents a self-contained window the user can move/resize.
/// </summary>
public abstract class DefaultWindow : Window, IDisposable
{
    public virtual Container Root { get; private set; } = new Panel() { Id = "_WindowDefault" };

    public bool BuiltElements { get; private set; } = false;

    public bool Initialized { get; private set; } = false;

    public bool Disposed { get; private set; } = false;

    /// <summary>
    /// Controls if the window can be closed with escape.
    /// </summary>
    public bool CloseOnEscape { get; set; } = true;

    /// <summary>
    /// True for windows that should keep receiving normal dt while gameplay is paused.
    /// PauseWindow should return true. Most gameplay windows should stay false.
    /// </summary>
    public virtual bool UpdatesWhileGameplayPaused => false;

    /// <summary>
    /// This will change the default root type (VerticalStackPanel).<para/>
    /// <strong>Only change this before building the ui!</strong>
    /// </summary>
    protected void SetRootType<T>() where T : Container, new()
    {
        if (BuiltElements)
            throw new Exception("Elements have already been built.");

        var root = new T() { Id = "_WindowDefault" };
        Root = root;
    }

    public void AddElement(Widget element)
        => Root.Widgets.Add(element);

    public void RemoveElement(Widget element)
        => Root.Widgets.Remove(element);

    public void RemoveElement(string id)
    {
        var element = Root.FindChildById(id);
        if (element is not null)
            RemoveElement(element);
    }

    public T? GetElement<T>(string id) where T : Widget 
        => Root.FindChildById<T>(id);

    public void Dispose()
    {
        if (Disposed)
            return;

        OnClose();
        Root.Widgets.Clear();
        Content = null;
        Disposed = true;
    }

    /// <summary>
    /// Use this method to build your UI. Remember, always put the Base.BuildUi() in the end of your code.
    /// </summary>
    public virtual void BuildElements()
    {
        if (BuiltElements)
            return;

        Content = Root;
        BuiltElements = true;
    }

    public virtual void Initialize()
    {
        if (Initialized)
            return;

        IoCManager.ResolveDependencies(this);
        Initialized = true;
    }

    public virtual void OnOpen()
    {
    }

    public virtual void Update(float dt)
    {
    }

    public virtual void Draw(float dt)
    {
    }

    public virtual void OnClose()
    {
    }

    public override void Close()
    {
        if (Disposed)
            return;

        Dispose();
        base.Close();
    }
}
