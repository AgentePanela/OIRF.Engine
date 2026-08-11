using Engine.Shared.Prototypes;

namespace Engine.Client.Graphics.Fonts;

/// <summary>
/// Groups up to 4 .ttf files (regular/bold/italic/boldItalic) under one family name.
/// </summary>
[Prototype("fontFamily")]
public sealed class FontFamilyPrototype : IPrototype
{
    public string Type => "fontFamily";

    [DataField("id", required: true)]
    public string ID { get; set; } = "";

    /// <summary>
    /// E.g: Arial.ttf
    /// </summary>
    [DataField("regular", required: true)]
    public string Regular { get; set; } = "";

    /// <summary>
    /// E.g: Arial-Bold.ttf
    /// </summary>
    [DataField("bold")]
    public string? Bold { get; set; }

    /// <summary>
    /// E.g: Arial-Italic.ttf
    /// </summary>
    [DataField("italic")]
    public string? Italic { get; set; }

    /// <summary>
    /// E.g: Arial-BoldItalic.ttf
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
