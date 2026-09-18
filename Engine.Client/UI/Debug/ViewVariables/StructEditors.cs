using System.Globalization;
using Engine.Shared.Debug.ViewVariables;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI.Debug.ViewVariables;

public sealed class Vector2Editor : VVEditorControl
{
    private readonly LineEdit _x;
    private readonly LineEdit _y;

    public Vector2Editor(VVEditorContext ctx) : base(ctx)
    {
        Orientation = Orientation.Horizontal;
        Separation = 4;

        var readOnly = !ctx.Member.CanWrite;
        _x = new LineEdit { HorizontalExpand = true, MinWidth = 50, ReadOnly = readOnly };
        _y = new LineEdit { HorizontalExpand = true, MinWidth = 50, ReadOnly = readOnly };

        _x.OnTextChanged += _ => MarkDirty();
        _y.OnTextChanged += _ => MarkDirty();
        _x.OnTextEntered += _ => Commit();
        _y.OnTextEntered += _ => Commit();

        AddChild(_x);
        AddChild(_y);
    }

    private void Commit()
    {
        if (!float.TryParse(_x.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(_y.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            Ctx.SetStatus("invalid number");
            MarkInvalid();
            return;
        }

        var vec = new Vector2(x, y);
        TryCommit(vec, $"{x},{y}");
    }

    protected override void ApplyValue(VVValue value)
    {
        if (value.Local is Vector2 v)
        {
            _x.Text = v.X.ToString(CultureInfo.InvariantCulture);
            _y.Text = v.Y.ToString(CultureInfo.InvariantCulture);
        }

        ClearInvalid();
    }

    protected override void OnCommitFailed() => MarkInvalid();

    private void MarkInvalid()
    {
        _x.OutlineColor = Color.Red;
        _y.OutlineColor = Color.Red;
    }

    private void ClearInvalid()
    {
        _x.OutlineColor = null;
        _y.OutlineColor = null;
    }
}

public sealed class Vector3Editor : VVEditorControl
{
    private readonly LineEdit _x;
    private readonly LineEdit _y;
    private readonly LineEdit _z;

    public Vector3Editor(VVEditorContext ctx) : base(ctx)
    {
        Orientation = Orientation.Horizontal;
        Separation = 4;

        var readOnly = !ctx.Member.CanWrite;
        _x = new LineEdit { HorizontalExpand = true, MinWidth = 40, ReadOnly = readOnly };
        _y = new LineEdit { HorizontalExpand = true, MinWidth = 40, ReadOnly = readOnly };
        _z = new LineEdit { HorizontalExpand = true, MinWidth = 40, ReadOnly = readOnly };

        foreach (var edit in new[] { _x, _y, _z })
        {
            edit.OnTextChanged += _ => MarkDirty();
            edit.OnTextEntered += _ => Commit();
            AddChild(edit);
        }
    }

    private void Commit()
    {
        if (!float.TryParse(_x.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(_y.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            !float.TryParse(_z.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
        {
            Ctx.SetStatus("invalid number");
            MarkInvalid();
            return;
        }

        var vec = new Vector3(x, y, z);
        TryCommit(vec, $"{x},{y},{z}");
    }

    protected override void ApplyValue(VVValue value)
    {
        if (value.Local is Vector3 v)
        {
            _x.Text = v.X.ToString(CultureInfo.InvariantCulture);
            _y.Text = v.Y.ToString(CultureInfo.InvariantCulture);
            _z.Text = v.Z.ToString(CultureInfo.InvariantCulture);
        }

        ClearInvalid();
    }

    protected override void OnCommitFailed() => MarkInvalid();

    private void MarkInvalid()
    {
        _x.OutlineColor = Color.Red;
        _y.OutlineColor = Color.Red;
        _z.OutlineColor = Color.Red;
    }

    private void ClearInvalid()
    {
        _x.OutlineColor = null;
        _y.OutlineColor = null;
        _z.OutlineColor = null;
    }
}

public sealed class Vector4Editor : VVEditorControl
{
    private readonly LineEdit _x;
    private readonly LineEdit _y;
    private readonly LineEdit _z;
    private readonly LineEdit _w;

    public Vector4Editor(VVEditorContext ctx) : base(ctx)
    {
        Orientation = Orientation.Horizontal;
        Separation = 4;

        var readOnly = !ctx.Member.CanWrite;
        _x = new LineEdit { HorizontalExpand = true, MinWidth = 30, ReadOnly = readOnly };
        _y = new LineEdit { HorizontalExpand = true, MinWidth = 30, ReadOnly = readOnly };
        _z = new LineEdit { HorizontalExpand = true, MinWidth = 30, ReadOnly = readOnly };
        _w = new LineEdit { HorizontalExpand = true, MinWidth = 30, ReadOnly = readOnly };

        foreach (var edit in new[] { _x, _y, _z, _w })
        {
            edit.OnTextChanged += _ => MarkDirty();
            edit.OnTextEntered += _ => Commit();
            AddChild(edit);
        }
    }

    private void Commit()
    {
        if (!float.TryParse(_x.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(_y.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            !float.TryParse(_z.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ||
            !float.TryParse(_w.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
        {
            Ctx.SetStatus("invalid number");
            MarkInvalid();
            return;
        }

        var vec = new Vector4(x, y, z, w);
        TryCommit(vec, $"{x},{y},{z},{w}");
    }

    protected override void ApplyValue(VVValue value)
    {
        if (value.Local is Vector4 v)
        {
            _x.Text = v.X.ToString(CultureInfo.InvariantCulture);
            _y.Text = v.Y.ToString(CultureInfo.InvariantCulture);
            _z.Text = v.Z.ToString(CultureInfo.InvariantCulture);
            _w.Text = v.W.ToString(CultureInfo.InvariantCulture);
        }

        ClearInvalid();
    }

    protected override void OnCommitFailed() => MarkInvalid();

    private void MarkInvalid()
    {
        _x.OutlineColor = Color.Red;
        _y.OutlineColor = Color.Red;
        _z.OutlineColor = Color.Red;
        _w.OutlineColor = Color.Red;
    }

    private void ClearInvalid()
    {
        _x.OutlineColor = null;
        _y.OutlineColor = null;
        _z.OutlineColor = null;
        _w.OutlineColor = null;
    }
}

public sealed class ColorEditor : VVEditorControl
{
    private readonly LineEdit _edit;
    private readonly PanelContainer _swatch;

    public ColorEditor(VVEditorContext ctx) : base(ctx)
    {
        Orientation = Orientation.Horizontal;
        Separation = 4;

        _edit = new LineEdit { HorizontalExpand = true, ReadOnly = !ctx.Member.CanWrite };
        _edit.OnTextChanged += _ => MarkDirty();
        _edit.OnTextEntered += OnEntered;

        _swatch = new PanelContainer { MinWidth = 24, MinHeight = 24 };

        AddChild(_edit);
        AddChild(_swatch);
    }

    private void OnEntered(string text)
    {
        if (!ViewVariablesConvert.TryParse(typeof(Color), text, out var parsed, out var error))
        {
            Ctx.SetStatus(error ?? "invalid color");
            _edit.OutlineColor = Color.Red;
            return;
        }

        TryCommit(parsed, text);
    }

    protected override void ApplyValue(VVValue value)
    {
        _edit.Text = value.Text;
        _edit.OutlineColor = null;

        if (value.Local is Color c)
            _swatch.Background = c;
    }

    protected override void OnCommitFailed() => _edit.OutlineColor = Color.Red;
}
