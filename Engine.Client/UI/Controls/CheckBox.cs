using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Microsoft.Xna.Framework;
using VAlign = Engine.Client.UI.VerticalAlignment;

namespace Engine.Client.UI;

/// <summary>
/// A type of toggleable button that also has a checkbox.
/// </summary>
public sealed partial class CheckBox : BaseButton
{
    private Label? _label;
    private readonly TextureRect _icon;

    /// <summary>
    /// Optional label shown to the right of the box. Empty draws no label at all.
    /// </summary>
    public string Text
    {
        get => _label?.Text ?? "";
        set
        {
            _label ??= new Label { VerticalAlignment = VAlign.Center };
            _label.Text = value;

            if (Content != _label)
                Content = _label;
        }
    }

    /// <summary>
    /// Sprite key drawn when unchecked.
    /// </summary>
    [StyleField("uncheckedKey", "EngineInternal/Interface/CheckboxUnchecked")]
    private string? _uncheckedKey;

    /// <summary>
    /// Sprite key drawn when checked.
    /// </summary>
    [StyleField("checkedKey", "EngineInternal/Interface/CheckboxChecked")]
    private string? _checkedKey;

    [StyleField("boxSize", 20f)]
    private float? _boxSize;

    /// <summary>
    /// Gap in pixels between the box and Content.
    /// </summary>
    [StyleField("separation", 6f)]
    private float? _separation;

    public CheckBox()
    {
        ToggleMode = true;
        StyleClasses.Add("checkBox");

        _icon = new TextureRect { Stretch = true };
        AddChild(_icon); // separate slot from Content, which is reserved for the label
    }

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        _icon.Measure(new Vector2(BoxSize, BoxSize));

        if (Content is null)
            return new Vector2(BoxSize, BoxSize);

        var contentAvailable = new Vector2(MathHelper.Max(0, availableSize.X - BoxSize - Separation), availableSize.Y);
        Content.Measure(contentAvailable);
        return new Vector2(BoxSize + Separation + Content.DesiredSize.X, MathHelper.Max(BoxSize, Content.DesiredSize.Y));
    }

    protected override void ArrangeCore(Rectangle finalRect)
    {
        var boxRect = new Rectangle(finalRect.X, finalRect.Y + (finalRect.Height - (int)BoxSize) / 2, (int)BoxSize, (int)BoxSize);
        _icon.Arrange(boxRect);

        if (Content is null)
            return;

        var contentRect = new Rectangle(
            finalRect.X + (int)BoxSize + (int)Separation, finalRect.Y,
            MathHelper.Max(0, finalRect.Width - (int)BoxSize - (int)Separation), finalRect.Height);

        Content.Arrange(contentRect);
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        _icon.Key = Pressed ? CheckedKey : UncheckedKey;
    }
}
