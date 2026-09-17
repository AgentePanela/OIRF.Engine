using System;
using System.Collections.Generic;
using Engine.Shared.Debug.ViewVariables;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI.Debug.ViewVariables;

public sealed class BoolEditor : VVEditorControl
{
    private readonly CheckBox _box;

    public BoolEditor(VVEditorContext ctx) : base(ctx)
    {
        _box = new CheckBox { Disabled = !ctx.Member.CanWrite };
        _box.OnToggled += pressed => TryCommit(pressed, pressed.ToString());
        AddChild(_box);
    }

    protected override void ApplyValue(VVValue value) => _box.Pressed = value.Local is true;
}

public sealed class StringEditor : VVEditorControl
{
    private readonly LineEdit _edit;

    public StringEditor(VVEditorContext ctx) : base(ctx)
    {
        _edit = new LineEdit { HorizontalExpand = true, ReadOnly = !ctx.Member.CanWrite };
        _edit.OnTextChanged += _ => MarkDirty();
        _edit.OnTextEntered += text => TryCommit(text, text);
        AddChild(_edit);
    }

    protected override void ApplyValue(VVValue value)
    {
        _edit.Text = value.Text;
        _edit.OutlineColor = null;
    }

    protected override void OnCommitFailed() => _edit.OutlineColor = Color.Red;
}

public sealed class NumericEditor : VVEditorControl
{
    private readonly LineEdit _edit;

    public NumericEditor(VVEditorContext ctx) : base(ctx)
    {
        _edit = new LineEdit { HorizontalExpand = true, ReadOnly = !ctx.Member.CanWrite };
        _edit.OnTextChanged += _ => MarkDirty();
        _edit.OnTextEntered += OnEntered;
        AddChild(_edit);
    }

    private void OnEntered(string text)
    {
        if (!ViewVariablesConvert.TryParse(Ctx.Member.LocalType!, text, out var parsed, out var error))
        {
            Ctx.SetStatus(error ?? "invalid value");
            _edit.OutlineColor = Color.Red;
            return;
        }

        TryCommit(parsed, text);
    }

    protected override void ApplyValue(VVValue value)
    {
        _edit.Text = value.Text;
        _edit.OutlineColor = null;
    }

    protected override void OnCommitFailed() => _edit.OutlineColor = Color.Red;
}

public sealed class EnumEditor : VVEditorControl
{
    private readonly OptionButton _option;
    private readonly string[] _names;

    public EnumEditor(VVEditorContext ctx, Type enumType) : base(ctx)
    {
        _names = Enum.GetNames(enumType);

        _option = new OptionButton { HorizontalExpand = true, Disabled = !ctx.Member.CanWrite };
        foreach (var name in _names)
            _option.AddItem(name);

        _option.OnItemSelected += i =>
        {
            if (ApplyingValue || i < 0 || i >= _names.Length)
                return;

            TryCommit(Enum.Parse(enumType, _names[i]), _names[i]);
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

// one checkbox per single-bit named value - OR's the checked ones together on commit
public sealed class FlagsEnumEditor : VVEditorControl
{
    private readonly Type _enumType;
    private readonly List<(long Bit, CheckBox Box)> _boxes = new();

    public FlagsEnumEditor(VVEditorContext ctx, Type enumType) : base(ctx)
    {
        _enumType = enumType;
        Orientation = Orientation.Vertical;

        foreach (var name in Enum.GetNames(enumType))
        {
            var raw = Convert.ToInt64(Enum.Parse(enumType, name));
            if (raw == 0 || (raw & (raw - 1)) != 0)
                continue;

            var box = new CheckBox { Text = name, Disabled = !ctx.Member.CanWrite };
            box.OnToggled += _ => Commit();
            _boxes.Add((raw, box));
            AddChild(box);
        }
    }

    private void Commit()
    {
        long combined = 0;
        foreach (var (bit, box) in _boxes)
        {
            if (box.Pressed)
                combined |= bit;
        }

        var parsed = Enum.ToObject(_enumType, combined);
        TryCommit(parsed, parsed.ToString() ?? "");
    }

    protected override void ApplyValue(VVValue value)
    {
        if (value.Local is null)
            return;

        var raw = Convert.ToInt64(value.Local);
        foreach (var (bit, box) in _boxes)
            box.Pressed = (raw & bit) == bit;
    }
}
