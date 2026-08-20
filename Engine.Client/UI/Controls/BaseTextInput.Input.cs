using Engine.Client.Inputs;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

public abstract partial class BaseTextInput
{
    // thanks claude for make a base class for using my line edit and text edit

    /// <summary>
    /// Screen position to caret index.
    /// </summary>
    protected abstract int HitTestIndex(Vector2 screenPos);

    /// <summary>
    /// Selection range a double-click at this index should make.
    /// </summary>
    protected abstract (int start, int end) GetWordBounds(int index);

    /// <summary>
    /// Selection range a triple-click at this index should make
    /// </summary>
    protected abstract (int start, int end) GetTripleClickBounds(int index);

    /// <summary>
    /// Keeps the caret visible by adjusting whatever scroll state the subclass tracks.
    /// </summary>
    protected abstract void ClampScroll();

    protected internal override CursorShape GetCursorShape(Vector2 point) => CursorShape.IBeam;

    protected internal override void MouseButtonDown(MouseButton button)
    {
        base.MouseButtonDown(button);
        if (button != MouseButton.Left)
            return;

        var mouse = IoCManager.Resolve<InputManager>().MouseScreenPosition;
        var clickIndex = HitTestIndex(mouse);

        switch (RegisterClick(clickIndex))
        {
            case 2:
                (_selectionAnchor, _caret) = GetWordBounds(clickIndex);
                break;
            case 3:
                (_selectionAnchor, _caret) = GetTripleClickBounds(clickIndex);
                break;
            default:
                _caret = _selectionAnchor = clickIndex;
                break;
        }

        _coalescingTyping = false;
        _coalescingDeleting = false;
        OnCaretChanged();
        ResetBlink();
    }

    protected internal override void MouseMove(Vector2 position)
    {
        if (!IoCManager.Resolve<InputManager>().MouseDown(MouseButton.Left))
            return;

        _caret = HitTestIndex(position);
        _coalescingTyping = false;
        _coalescingDeleting = false;
        OnCaretChanged();
        ClampScroll();
    }

    protected void HandleTypedChar(char character)
    {
        if (ReadOnly || char.IsControl(character))
            return;

        if (!_coalescingTyping)
            PushUndo();

        _coalescingTyping = true;
        _coalescingDeleting = false;

        InsertText(character.ToString());
        ResetBlink();
        ClampScroll();
    }

    protected void HandleBackspace()
    {
        if (ReadOnly)
            return;

        if (_caret != _selectionAnchor)
        {
            PushUndo();
            _coalescingTyping = false;
            _coalescingDeleting = false;
            DeleteSelection();
        }
        else if (_caret > 0)
        {
            if (!_coalescingDeleting)
                PushUndo();

            _coalescingDeleting = true;
            _coalescingTyping = false;

            var removeAt = _caret - 1;
            Text = Text.Remove(removeAt, 1);
            _caret = _selectionAnchor = removeAt;
            OnCaretChanged();
        }
    }

    protected void HandleDelete()
    {
        if (ReadOnly)
            return;

        if (_caret != _selectionAnchor)
        {
            PushUndo();
            _coalescingTyping = false;
            _coalescingDeleting = false;
            DeleteSelection();
        }
        else if (_caret < Text.Length)
        {
            if (!_coalescingDeleting)
                PushUndo();

            _coalescingDeleting = true;
            _coalescingTyping = false;

            Text = Text.Remove(_caret, 1);
        }
    }

    protected void SelectAll()
    {
        _selectionAnchor = 0;
        _caret = Text.Length;
        _coalescingTyping = false;
        _coalescingDeleting = false;
        OnCaretChanged();
    }

    protected void HandleCut()
    {
        if (ReadOnly)
        {
            CopySelection();
            return;
        }

        PushUndo();
        _coalescingTyping = false;
        _coalescingDeleting = false;
        CutSelection();
    }

    protected void HandlePaste()
    {
        if (ReadOnly)
            return;

        PushUndo();
        _coalescingTyping = false;
        _coalescingDeleting = false;
        Paste();
    }

    /// <summary>
    /// How clipboard text gets cleaned up before insertion.
    /// </summary>
    protected abstract string TransformPastedText(string clipboardText);

    private void Paste()
    {
        var clip = TextCopy.ClipboardService.GetText();
        if (string.IsNullOrEmpty(clip))
            return;

        InsertText(TransformPastedText(clip));
    }
}
