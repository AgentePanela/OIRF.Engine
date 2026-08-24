using Engine.Shared.IoC;

namespace Engine.Client.UI;

/// <summary>
/// Base for a scene own full-screen UI (HUD). Already handles IoC dependencies.
/// </summary>
public abstract partial class Layout : Overlay
{
    public Layout()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        IoCManager.ResolveDependencies(this);
    }
}
