using System;
using Engine.Shared.Debug.ViewVariables;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI.Debug.ViewVariables;

public sealed class ReadOnlyTextEditor : VVEditorControl
{
    private readonly Label _label;

    public ReadOnlyTextEditor(VVEditorContext ctx) : base(ctx)
    {
        _label = new Label 
        { 
            HorizontalExpand = true, 
            AutoWrap = false, 
            TextVerticalAlign = VerticalAlignment.Bottom,
            TextAlign = HorizontalAlignment.Center,
            FontSize = 13f
        };
        AddChild(_label);
    }

    protected override void ApplyValue(VVValue value)
    {
        _label.Text = value.Text;
        _label.Color = value.Kind == VVValueKind.Error ? Color.IndianRed : Color.LightGray;
    }
}

// a checkbox to toggle HasValue plus the editor for the underlying type
public sealed class NullableEditorDecorator : VVEditorControl
{
    private readonly VVEditorControl _inner;
    private readonly CheckBox _hasValue;

    public NullableEditorDecorator(VVEditorContext ctx, Type underlying) : base(ctx)
    {
        Orientation = Orientation.Horizontal;
        Separation = 4;

        _hasValue = new CheckBox { Text = "set", Disabled = !ctx.Member.CanWrite };
        _hasValue.OnToggled += pressed =>
        {
            _inner.Visible = pressed;

            if (pressed)
                MarkDirty(); // nothing was written yet
            else
                TryCommit(null, "null");
        };

        _inner = VVEditorRegistry.CreateForType(ctx, underlying);

        AddChild(_hasValue);
        AddChild(_inner);
    }

    protected override void ApplyValue(VVValue value)
    {
        _hasValue.Pressed = value.Kind != VVValueKind.Null;
        _inner.Visible = _hasValue.Pressed;

        if (_hasValue.Pressed)
            _inner.Refresh(value);
    }
}
