using System.Collections.Generic;
using Engine.Shared.Assets;
using Engine.Shared.Prototypes;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics.Fonts;

/// <summary>
/// Groups up to 4 .ttf files (regular/bold/italic/boldItalic) under one family name, or one sprite font.
/// </summary>
[Prototype("fontFamily")]
public sealed class FontFamilyPrototype : IPrototype
{
    public string Type => "fontFamily";

    [DataField("id", required: true)]
    public string ID { get; set; } = "";

    /// <summary>
    /// File name of the .ttf, without extension. E.g: Arial
    /// </summary>
    [DataField("regular")]
    public string Regular { get; set; } = "";

    /// <summary>
    /// Makes this family a sprite font, made from a texture.
    /// </summary>
    [DataField("sprite")]
    public SpriteFontDef? Sprite { get; set; }

    /// <summary>
    /// File name of the .ttf, without extension. E.g: Arial-Bold
    /// </summary>
    [DataField("bold")]
    public string? Bold { get; set; }

    /// <summary>
    /// File name of the .ttf, without extension. E.g: Arial-Italic
    /// </summary>
    [DataField("italic")]
    public string? Italic { get; set; }

    /// <summary>
    /// File name of the .ttf, without extension. E.g: Arial-BoldItalic
    /// </summary>
    [DataField("boldItalic")]
    public string? BoldItalic { get; set; }

    public string? GetFile(FontVariant variant) => variant switch
    {
        FontVariant.Regular => Regular,
        FontVariant.Bold => Bold,
        FontVariant.Italic => Italic,
        FontVariant.Bold | FontVariant.Italic => BoldItalic,
        _ => Regular,
    };
}

/// <summary>
/// A font drawn from a texture.
/// </summary>
public sealed class SpriteFontDef
{
    [DataField("texture", required: true), TextureKey]
    public string Texture { get; set; } = "";

    /// <summary>
    /// Size of one cell of the grid, in pixels.
    /// </summary>
    [DataField("letterSize", required: true)]
    public Point LetterSize { get; set; }

    /// <summary>
    /// The characters in the order of the grid, row by row.
    /// </summary>
    [DataField("chars", required: true)]
    public string Chars { get; set; } = "";

    /// <summary>
    /// Spacing between two glyphs.
    /// </summary>
    [DataField("spacing")]
    public int Spacing { get; set; } = 1;

    /// <summary>
    /// Spacing between two lines.
    /// </summary>
    [DataField("lineHeight")]
    public int LineHeight { get; set; }

    /// <summary>
    /// Width of some characters, when not the cell width. Ignored if <see cref="Proportional"/>.
    /// </summary>
    [DataField("widths")]
    public Dictionary<string, int>? Widths { get; set; }

    /// <summary>
    /// Trims the empty columns on both sides of each glyph.
    /// </summary>
    [DataField("proportional")]
    public bool Proportional { get; set; }

    /// <summary>
    /// Drawn in place of characters the font does not have.
    /// </summary>
    [DataField("fallback")]
    public string? Fallback { get; set; }
}
