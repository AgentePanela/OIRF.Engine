using HAlign = Engine.Client.UI.HorizontalAlignment;
using VAlign = Engine.Client.UI.VerticalAlignment;

namespace Engine.Client.UI;

/// <summary>
/// A clickable button showing a centered line of text.
/// </summary>
public sealed partial class Button : BaseButton
{
    private Label? _label;

    public string Text
    {
        get => _label?.Text ?? "";
        set
        {
            _label ??= new Label
            {
                HorizontalAlignment = HAlign.Center,
                VerticalAlignment = VAlign.Center,
                TextAlign = HAlign.Center,
                TextVerticalAlign = VAlign.Center,
            };

            _label.Text = value;

            if (Content != _label)
                Content = _label;
        }
    }

    public Button()
    {
        MinWidth = 32;
        MinHeight = 24;
    }

    public Button(string text) : this()
    {
        Text = text;
    } 
}
