using System.Diagnostics;
using Engine.Client.Graphics.Lighting;
using Engine.Client.Graphics.Shaders;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

public sealed partial class RenderManager
{
    private bool _bloom;
    private float _bloomThreshold;
    private float _bloomIntensity;

    private void InitBloom()
    {
        _cfg.Subs(LightingCvars.Bloom, v => _bloom = v);
        _cfg.Subs(LightingCvars.BloomThreshold, v => _bloomThreshold = MathHelper.Clamp(v, 0f, 1f));
        _cfg.Subs(LightingCvars.BloomIntensity, v => _bloomIntensity = MathHelper.Max(v, 0f));
    }

    // what is brighter than the threshold glows, over the world just drawn.
    private void ApplyBloom(RenderViewport viewport)
    {
        if (!_bloom || !viewport.BloomEnabled || !viewport.WasLit || viewport.Target is not { } frame
            || _shaders.BloomExtractInstance is not { } extract || _shaders.BloomBlurInstance is not { } blur
            || _shaders.BloomAddInstance is not { } add)
            return;

        var start = Stopwatch.GetTimestamp();
        var device = GameClient.GraphicsDevice;
        var levels = viewport.EnsureBloomTargets(device);

        // the bright part at half size, then smaller and smaller, each one blurred
        BloomPass(levels[0]!, frame, extract, new Vector4(_bloomThreshold, 0f, 0f, 0f));
        BloomBlur(levels[0]!, levels[1]!, blur);
        BloomPass(levels[2]!, levels[0]!, extract, Vector4.Zero);
        BloomBlur(levels[2]!, levels[3]!, blur);
        BloomPass(levels[4]!, levels[2]!, extract, Vector4.Zero);
        BloomBlur(levels[4]!, levels[5]!, blur);

        var old = viewport.SwapTargets(device);
        device.SetRenderTarget(viewport.Target);

        _batcher.Begin(Matrix.Identity, BlendState.Opaque, (float)GameClient.Timing.TotalTime);
        _batcher.SetState(_shaders.UnshadedInstance, SamplerState.PointClamp);
        _batcher.Draw(old, old.Bounds, old.Bounds, Color.White);

        _batcher.SetState(add, SamplerState.LinearClamp);
        _batcher.Custom0 = new Vector4(_bloomIntensity, 0f, 0f, 0f);
        for (var i = 0; i < levels.Length; i += 2)
            _batcher.Draw(levels[i]!, old.Bounds, levels[i]!.Bounds, Color.White);

        _batcher.Flush();
        _batcher.Custom0 = default;

        _frameStats.Light.BloomMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private void BloomPass(RenderTarget2D into, Texture2D from, ShaderInstance shader, Vector4 values)
    {
        var device = GameClient.GraphicsDevice;
        device.Textures[0] = null;
        device.SetRenderTarget(into);

        _batcher.Begin(Matrix.Identity, BlendState.Opaque, (float)GameClient.Timing.TotalTime);
        _batcher.SetState(shader, SamplerState.LinearClamp);
        _batcher.Custom0 = values;
        _batcher.Draw(from, into.Bounds, from.Bounds, Color.White);
        _batcher.Flush();
        _batcher.Custom0 = default;
    }

    // across into the other target
    private void BloomBlur(RenderTarget2D target, RenderTarget2D other, ShaderInstance blur)
    {
        BloomPass(other, target, blur, new Vector4(1f, 0f, 0f, 0f));
        BloomPass(target, other, blur, new Vector4(0f, 1f, 0f, 0f));
    }
}
