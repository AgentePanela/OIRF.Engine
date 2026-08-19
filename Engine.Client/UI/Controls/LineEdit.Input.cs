using Engine.Client.Inputs;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework.Input;

namespace Engine.Client.UI;

public sealed partial class LineEdit
{
    protected internal override void TextEntered(char character) => HandleTypedChar(character);

    protected override string TransformPastedText(string clipboardText)
        => clipboardText.Replace("\r", "").Replace("\n", ""); // no newlines - single line

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
                HandleBackspace();
                break;
            case Keys.Delete:
                HandleDelete();
                break;
            case Keys.Enter:
                OnTextEntered?.Invoke(Text);
                return; // doesn't move the caret
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
