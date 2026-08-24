using System;

namespace Engine.Client.Graphics.Fonts;

//warp fontstashsharp api
[Flags]
public enum TextDecoration
{
    None = 0,
    Underline = 1,
    Strikethrough = 2,
}

public static class TextDecorationExtensions
{
    public static FontStashSharp.TextStyle ToTextStyle(this TextDecoration decoration)
    {
        var result = FontStashSharp.TextStyle.None;
        if ((decoration & TextDecoration.Underline) != 0) result |= FontStashSharp.TextStyle.Underline;
        if ((decoration & TextDecoration.Strikethrough) != 0) result |= FontStashSharp.TextStyle.Strikethrough;
        return result;
    }
}
