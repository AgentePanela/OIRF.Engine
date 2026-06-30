using Engine.Shared.IoC;
using EngineTextStyle = Engine.Client.Graphics.Fonts.TextStyle;
using EngineTextStyleLibrary = Engine.Client.Graphics.Fonts.TextStyleLibrary;
using MyraButton = Myra.Graphics2D.UI.Button;
using MyraLabel = Myra.Graphics2D.UI.Label;

namespace Engine.Client.UI.Fonts;

/// <summary>
/// Small bridge that keeps Myra widget text creation aligned with the engine text style system.
/// This intentionally avoids unsupported deep Myra font overrides.
/// </summary>
public sealed class MyraFontBridge
{
    [Dependency] private readonly EngineTextStyleLibrary Styles = default!;

    public MyraFontBridge()
        => IoCManager.ResolveDependencies(this);

    public MyraLabel CreateLabel(string text, EngineTextStyle style = EngineTextStyle.Body)
    {
        var label = new MyraLabel
        {
            Text = text ?? string.Empty
        };

        ApplyStyle(label, style);
        return label;
    }

    public MyraButton CreateButton(string text, EngineTextStyle buttonStyle = EngineTextStyle.Button, 
        EngineTextStyle contentStyle = EngineTextStyle.ButtonText)
    {
        var button = new MyraButton
        {
            Content = CreateLabel(text, contentStyle)
        };

        ApplyStyle(button, buttonStyle);
        return button;
    }

    public void ApplyStyle(MyraLabel label, EngineTextStyle style)
    {
        var def = Styles.Get(style);
        label.TextColor = def.Color;
    }

    public void ApplyStyle(MyraButton button, EngineTextStyle style)
    {
        if (button.Content is MyraLabel label)
            ApplyStyle(label, style);
    }
}
