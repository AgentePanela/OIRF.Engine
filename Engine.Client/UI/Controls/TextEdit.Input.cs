using Engine.Client.Inputs;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework.Input;

namespace Engine.Client.UI;

public sealed partial class TextEdit
{
    protected internal override void TextEntered(char character) => HandleTypedChar(character); 
    protected override string TransformPastedText(string clipboardText) => clipboardText.Replace("\r", "");

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
            case Keys.Up:
                MoveCaretVertical(-1, shift);
                break;
            case Keys.Down:
                MoveCaretVertical(1, shift);
                break;
            case Keys.Home when ctrl:
                MoveCaret(0, shift);
                break;
            case Keys.End when ctrl:
                MoveCaret(Text.Length, shift);
                break;
            case Keys.Home:
            {
                // caret own VISUAL row
                var (line, column) = IndexToLineColumn(_caret);
                var visualLines = GetVisualLines();
                var v = visualLines[FindVisualLineIndex(visualLines, line, column)];
                MoveCaret(LineColumnToIndex(v.LogicalLine, v.Start), shift);
                break;
            }
            case Keys.End:
            {
                var (line, column) = IndexToLineColumn(_caret);
                var visualLines = GetVisualLines();
                var v = visualLines[FindVisualLineIndex(visualLines, line, column)];
                MoveCaret(LineColumnToIndex(v.LogicalLine, v.End), shift);
                break;
            }
            case Keys.Back:
                HandleBackspace();
                break;
            case Keys.Delete:
                HandleDelete();
                break;
            // Plain Enter inserts a newline
            case Keys.Enter when ctrl:
                OnTextEntered?.Invoke(Text);
                return;
            case Keys.Enter:
                if (ReadOnly)
                    return;

                PushUndo();
                _coalescingTyping = false;
                _coalescingDeleting = false;
                InsertText("\n");
                break;
            case Keys.Z when ctrl && shift:
                Redo();
                return;
            case Keys.Z when ctrl:
                Undo();
                return;
            case Keys.Y when ctrl:
                Redo();
                return;
            case Keys.A when ctrl:
                SelectAll();
                break;
            case Keys.C when ctrl:
                CopySelection();
                return;
            case Keys.X when ctrl:
                HandleCut();
                break;
            case Keys.V when ctrl:
                HandlePaste();
                break;
            default:
                return;
        }

        ResetBlink();
        ClampScroll();
    }
}
