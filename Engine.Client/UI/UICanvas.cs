using Myra.Graphics2D.UI;
using System;

namespace Engine.Client.UI;

/// <summary>
/// Represents a UI overlay that can display various widgets directly on top of the game-world.
/// </summary>
public abstract class UICanvas : IDisposable
{
    public virtual Container Root { get; private set; } = new Panel() { Id = "_CanvasUI" };

    public bool BuiltElements { get; private set; } = false;

    public bool Initialized { get; private set; } = false;

    public bool Disposed { get; private set; } = false;

    /// <summary>
    /// This will change the default root type (VerticalStackPanel).<para/>
    /// <strong>Only change this before building the ui!</strong>
    /// </summary>
    protected void SetRootType<T>() where T : Container, new()
    {
        if (BuiltElements)
            throw new Exception("UI is already built!");

        var root = new T() { Id = "_CanvasUI" };
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
        Disposed = true;
    }

    /// <summary>
    /// Use this method to build your UI. Remember, always put the Base.BuildUi() in the end of your code.
    /// </summary>
    public virtual void BuildElements()
    {
        if (BuiltElements)
            return;

        BuiltElements = true;
    }

    public virtual void Initialize()
    {
        if (Initialized)
            return;

        Initialized = true;
    }

    public virtual void Update(float dt) { }

    public virtual void Draw(float dt) { }

    public virtual void OnClose() { }
}
