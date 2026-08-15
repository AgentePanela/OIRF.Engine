using HAlign = Engine.Client.UI.HorizontalAlignment;
using VAlign = Engine.Client.UI.VerticalAlignment;

namespace Engine.Client.UI;

/// <summary>
/// A switch-style toggle button like a checkbox.
/// </summary>
public sealed partial class CheckButton : BaseButton
{
    private Label? _label;

    /// <summary>
    /// Optional label shown next to the toggle. Empty draws no label at all.
    /// </summary>
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

    public CheckButton()
    {
        ToggleMode = true;
    }
}
