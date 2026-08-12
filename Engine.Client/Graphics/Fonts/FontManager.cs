using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Engine.Shared.Assets;
using Engine.Shared.IoC;
using Engine.Shared.Prototypes;
using Engine.Shared.Storage;

namespace Engine.Client.Graphics.Fonts;

public sealed class FontManager : IFontManager
{
    public const float DefaultSize = 16f;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;

    private static readonly Dictionary<string, ResFile> _ttfFiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ResFile> _ttfFilesByName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string Family, FontVariant Variant), FontSystem> _fontSystems = new();
    private static bool _indexed;

    public readonly ResPath resPath = new("Fonts");

    public FontManager()
    {
        IoCManager.ResolveDependencies(this);

        if (_indexed)
            return;

        FontSystemDefaults.TextShaper = new HarfBuzzTextShaper();
        //! not supported rn
        //FontSystemDefaults.FontLoader = new FreeTypeLoader(); 
        _indexed = true;

        var ttfFiles = resPath.GetFiles("ttf");
        for (var index = 0; index < ttfFiles.Length; index++)
        {
            ref readonly var file = ref ttfFiles[index];
            _ttfFiles[file.Relative] = file;
            _ttfFilesByName[Path.GetFileNameWithoutExtension(file.Relative)] = file;
        }
    }

    public IReadOnlyCollection<string> Families
        => _protoMan.EnumerateAll<FontFamilyPrototype>().Select(p => p.ID).ToArray();

    public SpriteFontBase Get(float size = DefaultSize, FontVariant variant = FontVariant.Regular)
        => Get(size, DefaultFamily() ?? string.Empty, variant);

    public SpriteFontBase Get(float size, string family, FontVariant variant = FontVariant.Regular)
    {
        if (TryGetSystem(family, variant, out var system))
            return system.GetFont(size);

        // variant not configured for this family - fall back to Regular
        if (variant != FontVariant.Regular && TryGetSystem(family, FontVariant.Regular, out system))
            return system.GetFont(size);

        // family itself does not exist - fall back to the default family
        var defaultFamily = DefaultFamily();
        if (defaultFamily is not null && defaultFamily != family && TryGetSystem(defaultFamily, FontVariant.Regular, out system))
            return system.GetFont(size);

        throw new InvalidOperationException(
            $"No font family '{family}' (variant {variant}) loaded, and no fallback available. " +
            $"Known families: {(Families.Count == 0 ? "(none)" : string.Join(", ", Families))}");
    }

    public SpriteFontBase GetFallback() => Get(DefaultSize);

    public Vector2 Measure(string text, float size)
        => Get(size).MeasureString(text ?? string.Empty);

    public Vector2 Measure(string text, string font, float size, FontVariant variant = FontVariant.Regular)
        => Get(size, font, variant).MeasureString(text ?? string.Empty);

    private string? DefaultFamily()
        => _protoMan.EnumerateAll<FontFamilyPrototype>().FirstOrDefault()?.ID;

    private bool TryGetSystem(string font, FontVariant variant, [NotNullWhen(true)] out FontSystem? system)
    {
        if (_fontSystems.TryGetValue((font, variant), out system))
            return true;

        system = null;
        ResFile file;

        if (_protoMan.TryIndex<FontFamilyPrototype>(font, out var proto))
        {
            var fileName = proto.GetFile(variant);
            if (fileName is null || !_ttfFiles.TryGetValue(fileName, out file))
                return false;
        }
        // no fontFamily prototype registered under this name - last resort: a loose .ttf
        // whose file name (without extension) matches exactly. Only stands in for Regular;
        // a loose file has no bold/italic pairing to speak of.
        else if (variant == FontVariant.Regular && _ttfFilesByName.TryGetValue(font, out file))
        {
        }
        else
        {
            return false;
        }

        var created = new FontSystem();
        created.CurrentAtlasFull += (_, _) => created.Reset();
        created.AddFont(FileSystem.ReadAllBytes(file.FilePath));

        _fontSystems[(font, variant)] = created;
        system = created;
        return true;
    }
}
