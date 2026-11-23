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

    // the edges of the last DrawFovShadows
    private readonly List<ShadowSegment> _fovSegments = new();
    private Vector2 _fovFrom = new(float.NaN);
    private float _fovRadius;
    private int _fovVersion = -1;

    bool ILightRenderer.TryGetEyePosition(EntityUid eye, out Vector2 position)
    {
        position = default;
        if (!TryTransform(eye, out var transform))
            return false;

        position = transform.Position;
        return true;
    }

    void ILightRenderer.DrawFovShadows(Vector2 from, float radius, RectangleF bounds, float scale, Point targetSize,
        LightStats stats)
    {
        var shadowEffect = _shaders.GetShader(LightShadow);
        if (shadowEffect is null)
            return;

        // as far as the corner of the mask farthest from the eye
        var far = MathF.Max(
            MathF.Max(Vector2.Distance(from, new Vector2(bounds.Left, bounds.Top)),
                Vector2.Distance(from, new Vector2(bounds.Right, bounds.Top))),
            MathF.Max(Vector2.Distance(from, new Vector2(bounds.Left, bounds.Bottom)),
                Vector2.Distance(from, new Vector2(bounds.Right, bounds.Bottom))));
        radius = radius > 0f ? MathF.Min(radius, far) : far;

        var version = _occluders.OutlinesVersion;
        if (from != _fovFrom || radius != _fovRadius || version != _fovVersion)
        {
            _fovSegments.Clear();
            _occluders.CollectSegments(from, radius, _fovSegments);
            _fovFrom = from;
            _fovRadius = radius;
            _fovVersion = version;
        }

        stats.FovSegments += _fovSegments.Count;
        if (_fovSegments.Count == 0)
            return;

        var device = GameClient.GraphicsDevice;
        var slot = new Vector4((from.X - bounds.X) * scale, (from.Y - bounds.Y) * scale, scale, NoClip);

        // the edges of the lights are counted by Prepare
        var counted = _shadowBatch.Count;

        _shadowBatch.SetTarget(new Vector2(targetSize.X, targetSize.Y));
        foreach (var segment in _fovSegments)
            _shadowBatch.Add(device, shadowEffect, segment, from, radius, 0f, slot);
        _shadowBatch.Flush(device, shadowEffect);

        _shadowBatch.Count = counted;
    }
}
