using System;
using System.Collections.Generic;
using Engine.Shared.Common;
using Engine.Shared.Containers;
using Engine.Shared.GameObjects;
using Engine.Shared.Graphics;
using Engine.Shared.Lighting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

internal struct SpriteLightDraw
{
    public Texture2D Texture;

    /// <summary>
    /// The current frame of the layer on <see cref="Texture"/>.
    /// </summary>
    public Rectangle TexRegion;

    public Vector2 Center;
    public float Rotation;

    public Vector2 Size;

    public float Spread;
    public Color Color;
    public float Intensity;
}

public sealed class SpriteLightSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprites = default!;

    // reported once each
    private readonly HashSet<(EntityUid, string)> _missingLayers = new();

    /// <summary>
    /// Adds to <paramref name="lights"/> every SpriteLight layer whose light reaches <paramref name="area"/>.
    /// </summary>
    internal void CollectLights(RectangleF area, List<SpriteLightDraw> lights)
    {
        foreach (var (uid, comp, sprite, transform) in
                 GetEntitiesWithComp<SpriteLightComponent, SpriteComponent, TransformComponent>())
        {
            if (!comp.Enabled || !transform.Visible || HasComp<ContainedComponent>(uid))
                continue;

            foreach (var entry in comp.Lights)
            {
                if (entry.Intensity <= 0f)
                    continue;

                if (_sprites.GetLayer(sprite, entry.Layer) is not { } layer)
                {
                    if (_missingLayers.Add((uid, entry.Layer)))
                        Log.Warn($"SpriteLight of {GetName(uid)} ({uid}) wants layer '{entry.Layer}', which its sprite does not have");
                    continue;
                }

                if (layer.Visible && Measure(entry, layer, sprite, transform) is { } light && Reaches(light, area))
                    lights.Add(light);
            }
        }
    }

    private SpriteLightDraw? Measure(SpriteLightEntry entry, SpriteLayer layer, SpriteComponent sprite,
        TransformComponent transform)
    {
        if (_sprites.GetLayerSprite(layer, sprite) is not { CachedTexture: { } texture } frame)
            return null;

        var region = frame.CachedRegion;
        region.X += (int)frame.Offset.X;
        region.Y += (int)frame.Offset.Y;
        if (region.Width <= 0 || region.Height <= 0)
            return null;

        // where the layer is drawn: the middle of its frame, turned with the entity
        var angle = transform.Angle;
        var turn = Matrix.CreateRotationZ(angle);
        var at = transform.Position + sprite.Offset + Vector2.Transform(layer.Offset, turn);
        var size = new Vector2(region.Width, region.Height);
        var center = at + Vector2.Transform((size / 2f - frame.Origin) * frame.Scale, turn) + entry.Offset;

        var spread = MathF.Max(entry.Spread, 0f);
        var scale = new Vector2(MathF.Abs(frame.Scale.X), MathF.Abs(frame.Scale.Y));

        return new SpriteLightDraw
        {
            Texture = texture,
            TexRegion = region,
            Center = center,
            Rotation = angle,
            Size = size * scale + new Vector2(spread * 2f),
            Spread = spread,

            // the layer is drawn tinted by its color, so is its light
            Color = new Color(entry.Color.ToVector3() * layer.Color.ToVector3()),
            Intensity = entry.Intensity,
        };
    }

    // turned or not, the light is within its bigger side from its center
    internal static bool Reaches(in SpriteLightDraw light, RectangleF area)
    {
        var reach = MathF.Max(light.Size.X, light.Size.Y);
        return light.Center.X + reach >= area.Left && light.Center.X - reach <= area.Right
               && light.Center.Y + reach >= area.Top && light.Center.Y - reach <= area.Bottom;
    }
}
