using System;

namespace Engine.Client.Graphics.Fonts;

/// <summary>
/// Variant for a <see cref="FontFamilyPrototype"/> that can be combined on their own.
/// </summary>
[Flags]
public enum FontVariant
{
    Regular = 0,
    Bold = 1,
    Italic = 2,
}
