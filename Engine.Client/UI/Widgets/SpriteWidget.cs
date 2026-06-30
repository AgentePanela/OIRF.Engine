using Engine.Client.Assets;
using Engine.Client.Graphics;
using Engine.Shared.IoC;
using Myra.Graphics2D.TextureAtlases;
using Myra.Graphics2D.UI;

namespace Engine.Client.UI.Widgets;

/// <summary>
/// A widget that displays a <see cref="Sprite2D"/> from the texture atlas.
/// </summary>
public class SpriteWidget : Image
{
    private string? _spriteKey;
    private Sprite2D? _sprite;

    public SpriteWidget(string spriteKey)
    {
        SetSpriteKey(spriteKey);
    }

    public SpriteWidget(Sprite2D sprite)
    {
        SetSprite(sprite);
    }

    /// <summary>
    /// The current atlas sprite key. Setting this resolves the sprite from the atlas.
    /// </summary>
    public string? SpriteKey
    {
        get => _spriteKey;
        set
        {
            if (value is null)
            {
                _spriteKey = null;
                _sprite = null;
                Renderable = null;
                return;
            }

            SetSpriteKey(value);
        }
    }

    /// <summary>
    /// The current <see cref="Sprite2D"/>. Setting this updates the displayed texture region.
    /// </summary>
    public Sprite2D? Sprite
    {
        get => _sprite;
        set
        {
            if (value is null)
            {
                _spriteKey = null;
                _sprite = null;
                Renderable = null;
                return;
            }

            SetSprite(value.Value);
        }
    }

    private void SetSpriteKey(string spriteKey)
    {
        _spriteKey = spriteKey;

        var asset = IoCManager.Resolve<IAssetManager>();
        if (asset.GetSprite(spriteKey, out var sprite))
        {
            _sprite = sprite;
            ApplyTexture(spriteKey);
        }
    }

    private void SetSprite(Sprite2D sprite)
    {
        _sprite = sprite;
        _spriteKey = sprite.Key;
        ApplyTexture(sprite.Key);
    }

    private void ApplyTexture(string key)
    {
        var asset = IoCManager.Resolve<IAssetManager>();
        if (!asset.GetTexture(key, out var atlasSpr, out var atlasPage))
            return;

        Renderable = new TextureRegion(atlasPage.Texture, atlasSpr.Region);
        Width = atlasSpr.Width;
        Height = atlasSpr.Height;
    }
}
