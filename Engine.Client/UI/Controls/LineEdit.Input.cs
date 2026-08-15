using System;
using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Inputs;
using Engine.Shared.Common;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Engine.Client.UI;

/// <summary>
/// Single-line text input.
/// </summary>
public partial class LineEdit : PanelContainer
{
    protected internal override void MouseButtonDown(MouseButton button)
    {
        base.MouseButtonDown(button);
        if (button != MouseButton.Left)
            return;

        var mouse = IoCManager.Resolve<InputManager>().MouseScreenPosition;
        _caret = _selectionAnchor = HitTestCaret(mouse.X);
        ResetBlink();
    }

    protected internal override void MouseMove(Vector2 position)
    {
        if (!IoCManager.Resolve<InputManager>().MouseDown(MouseButton.Left))
            return;

        _caret = HitTestCaret(position.X);
        ClampScroll();
    }

    protected internal override void TextEntered(char character)
    {
        if (char.IsControl(character))
            return;

        if (_caret != _selectionAnchor)
            DeleteSelection();

        if (Text.Length >= MaxLength)
            return;

        Text = Text.Insert(_caret, character.ToString());
        _caret++;
        _selectionAnchor = _caret;

        ResetBlink();
        ClampScroll();
    }

    protected internal override void KeyDown(Keys key)
    {
        var input = IoCManager.Resolve<InputManager>();
        var shift = input.IsKeyDownRaw(Keys.LeftShift) || input.IsKeyDownRaw(Keys.RightShift);
        var ctrl = input.IsKeyDownRaw(Keys.LeftControl) || input.IsKeyDownRaw(Keys.RightControl);

        switch (key)
        {
            case Keys.Left:
                MoveCaret(_caret - 1, shift);
                break;
            case Keys.Right:
                MoveCaret(_caret + 1, shift);
                break;
            case Keys.Home:
                MoveCaret(0, shift);
                break;
            case Keys.End:
                MoveCaret(Text.Length, shift);
                break;
            case Keys.Back:
                if (_caret != _selectionAnchor)
                    DeleteSelection();
                else if (_caret > 0)
                {
                    var removeAt = _caret - 1;
                    Text = Text.Remove(removeAt, 1);
                    _caret = _selectionAnchor = removeAt;
                }
                break;
            case Keys.Delete:
                if (_caret != _selectionAnchor)
                    DeleteSelection();
                else if (_caret < Text.Length)
                    Text = Text.Remove(_caret, 1);
                break;
            case Keys.Enter:
                OnTextEntered?.Invoke(Text);
                return; // doesn't move the caret
            case Keys.A when ctrl:
                _selectionAnchor = 0;
                _caret = Text.Length;
                break;
            case Keys.C when ctrl:
                CopySelection();
                return;
            case Keys.X when ctrl:
                CutSelection();
                break;
            case Keys.V when ctrl:
                Paste();
                break;
            default:
                return;
        }

        ResetBlink();
        ClampScroll();
    }

    private void DeleteSelection()
    {
        if (_caret == _selectionAnchor)
            return;

        var start = Math.Min(_caret, _selectionAnchor);
        var end = Math.Max(_caret, _selectionAnchor);
        Text = Text.Remove(start, end - start);
        _caret = _selectionAnchor = start;
    }

    private void CopySelection()
    {
        if (_caret == _selectionAnchor)
            return;

        var start = Math.Min(_caret, _selectionAnchor);
        var length = Math.Abs(_caret - _selectionAnchor);
        TextCopy.ClipboardService.SetText(Text.Substring(start, length));
    }

    private void CutSelection()
    {
        CopySelection();
        DeleteSelection();
    }

    private void Paste()
    {
        var clip = TextCopy.ClipboardService.GetText();
        if (string.IsNullOrEmpty(clip))
            return;

        clip = clip.Replace("\r", "").Replace("\n", ""); // strip newlines

        if (_caret != _selectionAnchor)
            DeleteSelection();

        var room = MaxLength - Text.Length;
        if (room <= 0)
            return;

        if (clip.Length > room)
            clip = clip[..room];

        Text = Text.Insert(_caret, clip);
        _caret += clip.Length;
        _selectionAnchor = _caret;
    }
}