using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

public sealed partial class LightingSystem
{
    // fullscreen quad in clip space, for the post-process passes
    private struct ScreenVertex
    {
        public Vector2 Position;
        public Vector2 TexCoord;
        public const int SizeInBytes = 16;
        public static readonly VertexDeclaration Declaration = new(
            new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
            new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));
    }
    private static readonly ScreenVertex[] ScreenQuad =
    {
        new() { Position = new Vector2(-1, -1), TexCoord = new Vector2(0, 0) },
        new() { Position = new Vector2( 1, -1), TexCoord = new Vector2(1, 0) },
        new() { Position = new Vector2( 1,  1), TexCoord = new Vector2(1, 1) },
        new() { Position = new Vector2(-1, -1), TexCoord = new Vector2(0, 0) },
        new() { Position = new Vector2( 1,  1), TexCoord = new Vector2(1, 1) },
        new() { Position = new Vector2(-1,  1), TexCoord = new Vector2(0, 1) },
    };

    private DiskVertex[] _wallVerts = new DiskVertex[256 * 6];

    // parameter lookups by name are dictionary hits, cache them once
    private EffectParameter? _blSourceMap, _blSourceTexel, _blIsHorizontal, _blBlurScale;
    private EffectParameter? _wmBlurred, _wmViewProj, _wmBleedStrength;

    // blurs the lightmap at half res, then draws the occluder quads over
    // the lightmap replacing each wall pixel with the blurred value, so
    // walls show the glow of nearby lights (Robust's wall bleed)
    private void RunWallBleed(Matrix viewProj)
    {
        if (_wallMergeEffect is null || _wallBleed.A is null || _wallBleed.B is null || _lightmap.Target is null)
            return;

        int needed = _occluders.Count * 6;
        if (_wallVerts.Length < needed)
            Array.Resize(ref _wallVerts, needed);

        int n = 0;
        foreach (var occ in _occluders)
        {
            var b = occ.Bounds;
            var tl = new Vector2(b.Left, b.Top);
            var tr = new Vector2(b.Right, b.Top);
            var br = new Vector2(b.Right, b.Bottom);
            var bl = new Vector2(b.Left, b.Bottom);
            _wallVerts[n++] = new DiskVertex { WorldPos = tl };
            _wallVerts[n++] = new DiskVertex { WorldPos = tr };
            _wallVerts[n++] = new DiskVertex { WorldPos = br };
            _wallVerts[n++] = new DiskVertex { WorldPos = tl };
            _wallVerts[n++] = new DiskVertex { WorldPos = br };
            _wallVerts[n++] = new DiskVertex { WorldPos = bl };
        }
        if (n == 0) return;

        // Chaining n gaussians of sigma s gives sigma*sqrt(n), so scaling the
        // tap spacing by sqrt(2/n) keeps the total width fixed however many
        // iterations run - iterations buy kernel quality, not reach. Reach is
        // WallBleedRadius, which stretches the whole thing.
        int iterations = _lighting.WallBleedIterations;
        float blurScale = MathF.Sqrt(2f / iterations) * _lighting.WallBleedRadius;

        Texture2D source = _lightmap.Target;
        for (int i = 0; i < iterations; i++)
        {
            BlurPass(source, _wallBleed.A, 1f, blurScale);
            BlurPass(_wallBleed.A, _wallBleed.B, 0f, blurScale);
            source = _wallBleed.B;
        }

        _wmBlurred?.SetValue(_wallBleed.B);
        _wmViewProj?.SetValue(viewProj);
        _wmBleedStrength?.SetValue(_lighting.WallBleedStrength);

        GameClient.GraphicsDevice.SetRenderTarget(_lightmap.Target);
        GameClient.GraphicsDevice.Viewport = new Viewport(0, 0, _lightmap.Target.Width, _lightmap.Target.Height);
        GameClient.GraphicsDevice.BlendState = BlendState.Opaque;
        GameClient.GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GameClient.GraphicsDevice.RasterizerState = RasterizerState.CullNone;

        foreach (var pass in _wallMergeEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GameClient.GraphicsDevice.DrawUserPrimitives(
                PrimitiveType.TriangleList, _wallVerts, 0, n / 3, DiskVertex.Declaration);
        }

        GameClient.GraphicsDevice.BlendState = BlendState.AlphaBlend;
    }

    private void RunLightBlur()
    {
        if (_lightBlurEffect is null || _blurScratch.Target is null || _lightmap.Target is null)
            return;

        BlurPass(_lightmap.Target, _blurScratch.Target, 1f);
        BlurPass(_blurScratch.Target, _lightmap.Target, 0f);

        GameClient.GraphicsDevice.BlendState = BlendState.AlphaBlend;
    }

    private void BlurPass(Texture2D source, RenderTarget2D dest, float isHorizontal, float blurScale = 1f)
    {
        _blSourceMap?.SetValue(source);
        _blSourceTexel?.SetValue(new Vector2(1f / source.Width, 1f / source.Height));
        _blIsHorizontal?.SetValue(isHorizontal);
        _blBlurScale?.SetValue(blurScale);

        GameClient.GraphicsDevice.SetRenderTarget(dest);
        GameClient.GraphicsDevice.BlendState = BlendState.Opaque;
        // SpriteBatch leaves CullCounterClockwise on, which would cull the quad
        GameClient.GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        GameClient.GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GameClient.GraphicsDevice.Viewport = new Viewport(0, 0, dest.Width, dest.Height);

        foreach (var pass in _lightBlurEffect!.CurrentTechnique.Passes)
        {
            pass.Apply();
            GameClient.GraphicsDevice.DrawUserPrimitives(
                PrimitiveType.TriangleList, ScreenQuad, 0, 2, ScreenVertex.Declaration);
        }
    }
}
