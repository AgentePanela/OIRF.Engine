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

/// <summary>
/// A plain text box for a member whose type this build cannot resolve - a component that only exists on the other
/// side.
/// </summary>
public sealed class RawTextEditor : VVEditorControl
{
    private readonly LineEdit _edit;

    public RawTextEditor(VVEditorContext ctx) : base(ctx)
    {
        _edit = new LineEdit { HorizontalExpand = true, ReadOnly = !ctx.Member.CanWrite };
        _edit.OnTextChanged += _ => MarkDirty();
        _edit.OnTextEntered += text => TryCommit(null, text);
        AddChild(_edit);
    }

    protected override void ApplyValue(VVValue value)
    {
        _edit.Text = value.Text;
        _edit.OutlineColor = null;
    }

    protected override void OnCommitFailed() => _edit.OutlineColor = Color.Red;
}

/// <summary>
/// Same as <see cref="EnumEditor"/> for an enum whose <see cref="Type"/> is unavailable.
/// </summary>
public sealed class NamedEnumEditor : VVEditorControl
{
    private readonly OptionButton _option;
    private readonly string[] _names;

    public NamedEnumEditor(VVEditorContext ctx, System.Collections.Generic.IReadOnlyList<string> names) : base(ctx)
    {
        _names = [.. names];

        _option = new OptionButton { HorizontalExpand = true, Disabled = !ctx.Member.CanWrite };
        foreach (var name in _names)
            _option.AddItem(name);

        _option.OnItemSelected += i =>
        {
            if (ApplyingValue || i < 0 || i >= _names.Length)
                return;

            TryCommit(null, _names[i]);
        };

        AddChild(_option);
    }

    protected override void ApplyValue(VVValue value)
    {
        var idx = Array.IndexOf(_names, value.Text);
        if (idx >= 0)
            _option.Select(idx);
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
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            _inner.Visible = pressed;
#pragma warning restore CS8602 // Dereference of a possibly null reference.

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
