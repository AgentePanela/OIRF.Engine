using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Engine.Client.Assets;
using Engine.Shared.IoC;
using FontStashSharp;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics.Fonts;

public sealed partial class FontManager
{
    private bool TryGetSpriteFont(string family, [NotNullWhen(true)] out StaticSpriteFont? font)
    {
        if (_spriteFonts.TryGetValue(family, out font))
            return font is not null;

        if (!_protoMan.TryIndex<FontFamilyPrototype>(family, out var proto) || proto.Sprite is not { } def)
            return false;

        font = BuildSpriteFont(proto.ID, def);
        _spriteFonts[family] = font;
        return font is not null;
    }

    private static StaticSpriteFont? BuildSpriteFont(string family, SpriteFontDef def)
    {
        var cell = def.LetterSize;
        if (cell.X <= 0 || cell.Y <= 0)
        {
            Log.Warn($"Sprite font '{family}' has glyphSize {cell}, both sides must be above 0");
            return null;
        }

        var assets = IoCManager.Resolve<IAssetManager>();
        if (!assets.GetTexture(def.Texture, out var sprite, out var page))
        {
            Log.Warn($"Sprite font '{family}' uses texture '{def.Texture}', which is not loaded!");
            return null;
        }

        var columns = sprite.Width / cell.X;
        var rows = sprite.Height / cell.Y;
        var font = new StaticSpriteFont(cell.Y, def.LineHeight > 0 ? def.LineHeight : cell.Y + 1);
        var pixels = def.Proportional ? new Color[cell.X * cell.Y] : null;

        var index = 0;
        foreach (var rune in def.Chars.EnumerateRunes())
        {
            if (index >= columns * rows)
            {
                Log.Warn($"Sprite font '{family}' has more chars than its texture has cells ({columns * rows})!");
                break;
            }

            var cellPos = new Point(sprite.Region.X + index % columns * cell.X, sprite.Region.Y + index / columns * cell.Y);
            index++;

            var left = 0;
            var width = cell.X;

            if (pixels is not null)
            {
                page.Texture.GetData(0, new Rectangle(cellPos, cell), pixels, 0, pixels.Length);
                Trim(pixels, cell, ref left, ref width);
            }
            else if (def.Widths is not null && def.Widths.TryGetValue(rune.ToString(), out var custom))
            {
                width = Math.Clamp(custom, 0, cell.X);
            }

            font.Glyphs[rune.Value] = new FontGlyph
            {
                Codepoint = rune.Value,
                Id = rune.Value,
                Texture = page.Texture,
                TextureOffset = new Point(cellPos.X + left, cellPos.Y),
                Size = new Point(width, cell.Y),
                XAdvance = width + def.Spacing,
            };
        }

        // characters with a width but no cell only take room
        if (def.Widths is not null)
        {
            foreach (var (chars, width) in def.Widths)
            {
                if (chars.Length == 0)
                    continue;

                var codepoint = Rune.GetRuneAt(chars, 0).Value;
                if (!font.Glyphs.ContainsKey(codepoint))
                    AddEmptyGlyph(font, codepoint, Math.Max(width, 0) + def.Spacing);
            }
        }

        if (!font.Glyphs.ContainsKey(' '))
            AddEmptyGlyph(font, ' ', cell.X + def.Spacing);

        if (!string.IsNullOrEmpty(def.Fallback))
            font.DefaultCharacter = Rune.GetRuneAt(def.Fallback, 0).Value;

        return font;
    }

    private static void AddEmptyGlyph(StaticSpriteFont font, int codepoint, int advance)
    {
        font.Glyphs[codepoint] = new FontGlyph
        {
            Codepoint = codepoint,
            Id = codepoint,
            XAdvance = advance,
        };
    }

    // drops the empty columns on both sides; a glyph with nothing in it keeps the whole cell
    private static void Trim(Color[] pixels, Point cell, ref int left, ref int width)
    {
        var first = -1;
        var last = -1;

        for (var x = 0; x < cell.X; x++)
        {
            for (var y = 0; y < cell.Y; y++)
            {
                if (pixels[y * cell.X + x].A == 0)
                    continue;

                if (first < 0)
                    first = x;
                last = x;
                break;
            }
        }

        if (first < 0)
            return;

        left = first;
        width = last - first + 1;
    }
}
