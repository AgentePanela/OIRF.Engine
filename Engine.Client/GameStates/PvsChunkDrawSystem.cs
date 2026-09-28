using System;
using Engine.Client.Graphics;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Graphics.Shaders;
using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.GameStates;

public sealed class PvsChunkDrawSystem : EntityDrawSystem
{
    private static readonly Color InRangeColor = new(120, 255, 120, 90);
    private static readonly Color InMarginColor = new(255, 220, 120, 90);
    private static readonly Color OutsideColor = new(255, 255, 255, 25);
    private static readonly Color RangeRingColor = new(120, 255, 120, 180);
    private static readonly Color MarginRingColor = new(255, 220, 120, 180);

    [Dependency] private readonly RenderManager _renderer = default!;
    [Dependency] private readonly Camera2D _camera = default!;
    [Dependency] private readonly IFontManager _fonts = default!;
    [Dependency] private readonly ShaderManager _shaderMan = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    private const int MaxChunksDrawn = 4096;

    public bool Enabled { get; set; }

    private Effect? _unshaded;
    private bool _gridTooBigLogged;

    private int _chunkSize;
    private float _range;
    private float _margin;

    public override void Init()
    {
        base.Init();

        _unshaded = _shaderMan.GetShader("Unshaded");

        _cfg.Subs(NetworkingCvars.NetPvsChunkSize, value => _chunkSize = value);
        _cfg.Subs(NetworkingCvars.NetPvsRange, value => _range = value);
        _cfg.Subs(NetworkingCvars.NetPvsLeaveMargin, value => _margin = value);
    }

    public override void Draw(float dt)
    {
        base.Draw(dt);

        if (!Enabled || _chunkSize <= 0)
            return;

        var eye = _camera.WorldCenter;
        var leaveRange = _range + _margin;

        DrawGrid(eye, leaveRange);

        _renderer.DrawCircle(eye, _range, borderColor: RangeRingColor, thickness: 2f, unshaded: true);
        _renderer.DrawCircle(eye, leaveRange, borderColor: MarginRingColor, thickness: 2f, unshaded: true);

        var label = $"pvs r={_range:0} +{_margin:0} chunk={_chunkSize}";
        _renderer.Submit(new RenderQueue(new Label2D(_fonts.Get(12f), label) { Color = RangeRingColor, Layer = 9999 },
            eye, shader: _unshaded));
    }

    private void DrawGrid(Vector2 eye, float leaveRange)
    {
        var min = PvsChunks.ToChunk(new Vector2(
            MathF.Min(_camera.ViewportLeft, eye.X - leaveRange),
            MathF.Min(_camera.ViewportTop, eye.Y - leaveRange)), _chunkSize);

        var max = PvsChunks.ToChunk(new Vector2(
            MathF.Max(_camera.ViewportRight, eye.X + leaveRange),
            MathF.Max(_camera.ViewportBottom, eye.Y + leaveRange)), _chunkSize);

        // a small chunk size with a big range would be tens of thousands of rects a frame
        if ((long)(max.X - min.X + 1) * (max.Y - min.Y + 1) > MaxChunksDrawn)
        {
            if (!_gridTooBigLogged)
            {
                _gridTooBigLogged = true;
                Log.Warn($"{_range} range over {_chunkSize} chunks is more than {MaxChunksDrawn} chunks to draw, showing only the radii.");
            }

            return;
        }

        var rangeSq = _range * _range;
        var leaveSq = leaveRange * leaveRange;

        for (var cx = min.X; cx <= max.X; cx++)
        for (var cy = min.Y; cy <= max.Y; cy++)
        {
            var distSq = PvsChunks.DistanceSqToChunk(eye, cx, cy, _chunkSize);

            var color = distSq <= rangeSq
                ? InRangeColor
                : distSq <= leaveSq
                    ? InMarginColor
                    : OutsideColor;

            var rect = new Rectangle(cx * _chunkSize, cy * _chunkSize, _chunkSize, _chunkSize);
            _renderer.DrawRect(rect, borderColor: color, thickness: 1f, unshaded: true);
        }
    }
}
