using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Engine.Client.Graphics;
using Engine.Shared.GameObjects.Factories;
using Engine.Shared.IoC;
using Engine.Shared.Prototypes;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class EntityPrototypeExtensions
{
    public static bool TryGetBaseSprite(
        this EntityPrototype proto,
        [NotNullWhen(true)] out Sprite2D? sprite)
    {
        sprite = null;

        var key = proto.TryGetBaseSpriteKey();

        if (key is null)
            return false;

        sprite = Sprite2D.GetFromAtlas(key);

        return true;
    }

    public static string? TryGetBaseSpriteKey(
        this EntityPrototype proto)
    {
        var factory =
            IoCManager.Resolve<ComponentFactory>();

        var spriteType =
            factory.GetSanitizedByType<SpriteComponent>();

        if (spriteType is null)
            return null;

        if (!proto.TryGetComponentEntry(spriteType, out var component))
            return null;

        if (!component.TryGet<string>("Key", out var key))
            return null;

        return key;
    }
}