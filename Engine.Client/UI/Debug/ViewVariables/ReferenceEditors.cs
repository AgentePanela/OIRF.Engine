using Engine.Shared.Debug.ViewVariables;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI.Debug.ViewVariables;

public sealed class EntityUidEditor : VVEditorControl
{
    private readonly LineEdit _edit;

    public EntityUidEditor(VVEditorContext ctx) : base(ctx)
    {
        _edit = new LineEdit { HorizontalExpand = true, ReadOnly = !ctx.Member.CanWrite };
        _edit.OnTextChanged += _ => MarkDirty();
        _edit.OnTextEntered += OnEntered;
        AddChild(_edit);
    }

    private void OnEntered(string text)
    {
        if (!ViewVariablesConvert.TryParse(typeof(EntityUid), text, out var parsed, out var error))
        {
            Ctx.SetStatus(error ?? "invalid entity uid");
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
