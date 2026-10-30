using System;
using System.Collections.Generic;
using Engine.Shared.Common;
using Engine.Shared.GameObjects;
using Engine.Shared.Graphics;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics.Lighting;

public sealed partial class LightingSystem
{
    // the shadows of the eye are never cut, the mask is all theirs
    private readonly float NoClip = 1e7f;
    private readonly ProtoId<ShaderPrototype> LightShadow = "LightShadow";

    bool ILightRenderer.DrawFovMask(EntityUid eye, Vector2 offset, RectangleF bounds, float scale, Point targetSize,
        LightStats stats, out Vector2 eyePosition)
    {
        eyePosition = default;
        if (!TryTransform(eye, out var transform))
            return false;

        eyePosition = transform.Position + offset;

        var shadowEffect = _shaders.GetShader(LightShadow);
        if (shadowEffect is null)
            return false;

        // as far as the corner of the mask farthest from the eye
        var center = eyePosition;
        var radius = MathF.Max(
            MathF.Max(Vector2.Distance(center, new Vector2(bounds.Left, bounds.Top)),
                Vector2.Distance(center, new Vector2(bounds.Right, bounds.Top))),
            MathF.Max(Vector2.Distance(center, new Vector2(bounds.Left, bounds.Bottom)),
                Vector2.Distance(center, new Vector2(bounds.Right, bounds.Bottom))));

        _segments.Clear();
        _occluders.CollectFovSegments(center, radius, _segments);
        stats.FovSegments += _segments.Count;
        if (_segments.Count == 0)
            return true;

        var device = GameClient.GraphicsDevice;
        var slot = new Vector4((center.X - bounds.X) * scale, (center.Y - bounds.Y) * scale, scale, NoClip);

        // the edges of the lights are counted by Prepare
        var counted = _shadowBatch.Count;

        _shadowBatch.SetTarget(new Vector2(targetSize.X, targetSize.Y));
        foreach (var segment in _segments)
            _shadowBatch.Add(device, shadowEffect, segment, center, radius, 0f, slot);
        _shadowBatch.Flush(device, shadowEffect);

        _shadowBatch.Count = counted;
        return true;
    }
}
