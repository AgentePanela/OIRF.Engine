namespace Engine.Client.UI;

/// <summary>
/// Base for a game interface overlay drawed above the default interface.
/// </summary>
public abstract partial class Overlay : PanelContainer
{
    protected Overlay()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
    }
}
