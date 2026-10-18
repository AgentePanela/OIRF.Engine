using System;
using System.Runtime.InteropServices;
using Engine.Client.Graphics.Shaders;
using Engine.Shared.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Batching;

/// <summary>
/// What every corner of a sprite sends to the shader, see Sprite.fxh.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SpriteVertex : IVertexType
{
    public Vector2 Position;
    public Color Color;
    public Vector2 UV;
    public Vector2 LocalUV;
    public Vector4 Custom0;
    public Vector4 Custom1;

    /// <summary>
    /// xy: the world position the light is read at. z: emission. w: unused for now.
    /// </summary>
    public Vector4 Light;

    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
        new VertexElement(8, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        new VertexElement(12, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(20, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1),
        new VertexElement(28, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 2),
        new VertexElement(44, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 3),
        new VertexElement(60, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 4));

    readonly VertexDeclaration IVertexType.VertexDeclaration => Declaration;
}

/// <summary>
/// Engine's own implementation of the Monogame SpriteBatch
/// </summary>
internal sealed class SpriteBatcher
{
    // 4 vertices each, and indices are 16 bit
    private const int MaxQuads = 8192;
    private const int InitialQuads = 256;

    private readonly GraphicsDevice _device;
    private readonly SpriteEffect _defaultEffect;
    private readonly RenderStats _stats;
    private readonly short[] _indices = new short[MaxQuads * 6];

    private SpriteVertex[] _vertices = new SpriteVertex[InitialQuads * 4];
    private int _quads;

    // state of the quads waiting in _vertices
    private Texture2D? _texture;
    private ShaderInstance? _shader;
    private SamplerState _sampler = SamplerState.PointClamp;
    private BlendState _blend = BlendState.AlphaBlend;
    private DepthStencilState _stencil = DepthStencilState.None;

    private BlendState? _blendOverride;

    private BlendState _defaultBlend = BlendState.AlphaBlend;
    private Matrix _view;
    private Matrix _projection;
    private Vector2 _screenSize;
    private float _time;

    /// <summary>
    /// What quads drawn from now on carry in their two custom values.
    /// </summary>
    public Vector4 Custom0;

    /// <inheritdoc cref="Custom0"/>
    public Vector4 Custom1;

    /// <summary>
    /// How the quads drawn from now on take the light.
    /// </summary>
    public TextureLighting Lighting;

    /// <summary>
    /// The light the quads are drawn with, null while nothing is lit (nothing gets darker then).
    /// </summary>
    public Texture2D? LightMap;

    /// <summary>
    /// Takes a world position to its uv on <see cref="LightMap"/>: uv = position * xy + zw.
    /// </summary>
    public Vector4 LightMapTransform;
    public Vector4 LightParams;

    /// <summary>
    /// What quads with no shader of their own are drawn with. The plain SpriteEffect, which ignores the light, while
    /// null (before the shaders are loaded).
    /// </summary>
    public ShaderInstance? DefaultShader;
    public Texture2D White { get; }

    private static readonly Vector4 NoLightParams = new(1f, 0f, 1f, 0f);

    // so far up nothing is ever above it
    private const float NoLightCap = -1e30f;

    public SamplerState Sampler => _sampler;

    public SpriteBatcher(GraphicsDevice device, RenderStats stats)
    {
        _device = device;
        _stats = stats;
        _defaultEffect = new SpriteEffect(device);

        White = new Texture2D(device, 1, 1);
        White.SetData(new[] { Color.White });

        for (var i = 0; i < MaxQuads; i++)
        {
            var v = i * 4;
            var n = i * 6;

            // top left, top right, bottom left / top right, bottom right, bottom left
            _indices[n] = (short)v;
            _indices[n + 1] = (short)(v + 1);
            _indices[n + 2] = (short)(v + 2);
            _indices[n + 3] = (short)(v + 1);
            _indices[n + 4] = (short)(v + 3);
            _indices[n + 5] = (short)(v + 2);
        }
    }

    /// <summary>
    /// Starts drawing into the target that is set. <paramref name="view"/> takes world positions to its pixels.
    /// </summary>
    public void Begin(Matrix view, BlendState defaultBlend, float time)
    {
        var vp = _device.Viewport;

        _view = view;
        _projection = Matrix.CreateOrthographicOffCenter(0, vp.Width, vp.Height, 0, 0, -1);
        _screenSize = new Vector2(vp.Width, vp.Height);
        _defaultBlend = defaultBlend;
        _time = time;

        _texture = null;
        _shader = null;
        _blend = defaultBlend;
        _stencil = DepthStencilState.None;
        _blendOverride = null;
        Custom0 = Custom1 = default;
        Lighting = default;
    }

    /// <summary>
    /// Sets the stencil the next quads are drawn with, and a blend that replaces the shader's one while not null.
    /// </summary>
    public void SetStencil(DepthStencilState stencil, BlendState? blendOverride)
    {
        if (ReferenceEquals(stencil, _stencil) && ReferenceEquals(blendOverride, _blendOverride))
            return;

        if (_quads > 0)
        {
            _stats.BreaksByMask++;
            Flush();
        }

        _stencil = stencil;
        _blendOverride = blendOverride;
    }

    /// <summary>
    /// Sets what the next quads are drawn with. The ones waiting are sent first when it is not what they use.
    /// </summary>
    public void SetState(ShaderInstance? shader, SamplerState sampler)
    {
        // an instance with no effect draws like no shader at all
        if (shader?.Effect is null)
            shader = null;

        var blend = shader is null ? _defaultBlend : ToBlendState(shader.Blend);

        if (_quads > 0)
        {
            if (!ReferenceEquals(shader, _shader))
            {
                _stats.BreaksByShader++;
                Flush();
            }
            else if (!ReferenceEquals(sampler, _sampler))
            {
                _stats.BreaksBySampler++;
                Flush();
            }
            else if (!ReferenceEquals(blend, _blend))
            {
                _stats.BreaksByBlend++;
                Flush();
            }
        }

        _shader = shader;
        _sampler = sampler;
        _blend = blend;
    }

    public void SetSampler(SamplerState sampler)
    {
        if (ReferenceEquals(sampler, _sampler))
            return;

        if (_quads > 0)
        {
            _stats.BreaksBySampler++;
            Flush();
        }

        _sampler = sampler;
    }

    /// <summary>
    /// A region of a texture, placed the way SpriteBatch places it: <paramref name="origin"/> is in source pixels
    /// and is what sits on <paramref name="position"/> and what the quad rotates and scales around.
    /// </summary>
    public void Draw(Texture2D texture, Vector2 position, Rectangle source, Color color, float rotation, Vector2 origin,
        Vector2 scale, SpriteEffects effects)
    {
        var w = source.Width * scale.X;
        var h = source.Height * scale.Y;
        var ox = -origin.X * scale.X;
        var oy = -origin.Y * scale.Y;

        Vector2 tl, tr, bl, br;
        if (rotation == 0f)
        {
            tl = new Vector2(position.X + ox, position.Y + oy);
            tr = new Vector2(tl.X + w, tl.Y);
            bl = new Vector2(tl.X, tl.Y + h);
            br = new Vector2(tl.X + w, tl.Y + h);
        }
        else
        {
            var (sin, cos) = MathF.SinCos(rotation);
            tl = new Vector2(position.X + ox * cos - oy * sin, position.Y + ox * sin + oy * cos);
            tr = new Vector2(position.X + (ox + w) * cos - oy * sin, position.Y + (ox + w) * sin + oy * cos);
            bl = new Vector2(position.X + ox * cos - (oy + h) * sin, position.Y + ox * sin + (oy + h) * cos);
            br = new Vector2(position.X + (ox + w) * cos - (oy + h) * sin, position.Y + (ox + w) * sin + (oy + h) * cos);
        }

        DrawQuad(texture, tl, tr, bl, br, source, color, effects);
    }

    /// <summary>
    /// A region of a texture stretched over <paramref name="dest"/>.
    /// </summary>
    public void Draw(Texture2D texture, Rectangle dest, Rectangle source, Color color)
    {
        DrawQuad(texture,
            new Vector2(dest.Left, dest.Top), new Vector2(dest.Right, dest.Top),
            new Vector2(dest.Left, dest.Bottom), new Vector2(dest.Right, dest.Bottom),
            source, color, SpriteEffects.None);
    }

    private void DrawQuad(Texture2D texture, Vector2 tl, Vector2 tr, Vector2 bl, Vector2 br, Rectangle source,
        Color color, SpriteEffects effects)
    {
        var invW = 1f / texture.Width;
        var invH = 1f / texture.Height;

        float u0 = source.X * invW, u1 = (source.X + source.Width) * invW;
        float v0 = source.Y * invH, v1 = (source.Y + source.Height) * invH;
        float l0 = 0f, l1 = 1f, m0 = 0f, m1 = 1f;

        // the local uv follows the texture, a flipped sprite has it flipped too
        if ((effects & SpriteEffects.FlipHorizontally) != 0)
        {
            (u0, u1) = (u1, u0);
            (l0, l1) = (l1, l0);
        }

        if ((effects & SpriteEffects.FlipVertically) != 0)
        {
            (v0, v1) = (v1, v0);
            (m0, m1) = (m1, m0);
        }

        var i = Reserve(texture);
        Set(ref _vertices[i], tl, color, new Vector2(u0, v0), new Vector2(l0, m0));
        Set(ref _vertices[i + 1], tr, color, new Vector2(u1, v0), new Vector2(l1, m0));
        Set(ref _vertices[i + 2], bl, color, new Vector2(u0, v1), new Vector2(l0, m1));
        Set(ref _vertices[i + 3], br, color, new Vector2(u1, v1), new Vector2(l1, m1));
    }

    /// <summary>
    /// A quad whose corners were already worked out by someone else (the text renderer).
    /// </summary>
    public void DrawQuad(Texture2D texture, in VertexPositionColorTexture tl, in VertexPositionColorTexture tr,
        in VertexPositionColorTexture bl, in VertexPositionColorTexture br)
    {
        var i = Reserve(texture);
        Set(ref _vertices[i], new Vector2(tl.Position.X, tl.Position.Y), tl.Color, tl.TextureCoordinate, new Vector2(0f, 0f));
        Set(ref _vertices[i + 1], new Vector2(tr.Position.X, tr.Position.Y), tr.Color, tr.TextureCoordinate, new Vector2(1f, 0f));
        Set(ref _vertices[i + 2], new Vector2(bl.Position.X, bl.Position.Y), bl.Color, bl.TextureCoordinate, new Vector2(0f, 1f));
        Set(ref _vertices[i + 3], new Vector2(br.Position.X, br.Position.Y), br.Color, br.TextureCoordinate, new Vector2(1f, 1f));
    }

    private void Set(ref SpriteVertex vertex, Vector2 position, Color color, Vector2 uv, Vector2 localUv)
    {
        vertex.Position = position;
        vertex.Color = color;
        vertex.UV = uv;
        vertex.LocalUV = localUv;
        vertex.Custom0 = Custom0;
        vertex.Custom1 = Custom1;

        // interpolated over the quad: a pixel of a Line sprite reads at its own x on the base, a Point one at the base
        var at = Lighting.Mode switch
        {
            LightBaseMode.Line => new Vector2(position.X, Lighting.Base.Y),
            LightBaseMode.Point => Lighting.Base,
            _ => position,
        };

        // the highest a pixel reads the light at, only a PixelCapped sprite has one
        var cap = Lighting.Mode == LightBaseMode.PixelCapped ? Lighting.Base.Y : NoLightCap;
        vertex.Light = new Vector4(at.X, at.Y, Lighting.Emission, cap);
    }

    // makes room for one quad of this texture and returns where its first vertex goes
    private int Reserve(Texture2D texture)
    {
        if (_quads > 0 && !ReferenceEquals(texture, _texture))
        {
            _stats.BreaksByTexture++;
            Flush();
        }
        else if (_quads == MaxQuads)
        {
            Flush();
        }

        _texture = texture;

        var index = _quads * 4;
        if (index + 4 > _vertices.Length)
            Array.Resize(ref _vertices, Math.Min(_vertices.Length * 2, MaxQuads * 4));

        _quads++;
        return index;
    }

    /// <summary>
    /// Sends the quads that are waiting to the GPU.
    /// </summary>
    public void Flush()
    {
        if (_quads == 0 || _texture is null)
            return;

        Effect effect;
        var shader = _shader ?? DefaultShader;
        var readsLight = false;
        if (shader?.Effect is { } custom)
        {
            effect = custom;
            shader.MatrixParam?.SetValue(_view * _projection);
            shader.TexelSizeParam?.SetValue(new Vector2(1f / _texture.Width, 1f / _texture.Height));
            shader.TimeParam?.SetValue(_time);
            shader.ScreenSizeParam?.SetValue(_screenSize);

            if (shader.LightMapParam is { } lightMap)
            {
                readsLight = true;
                lightMap.SetValue(LightMap ?? White);
                shader.LightMapTransformParam?.SetValue(LightMapTransform);
                shader.LightParamsParam?.SetValue(LightMap is null ? NoLightParams : LightParams);
            }
        }
        else
        {
            _defaultEffect.TransformMatrix = _view;
            effect = _defaultEffect;
        }

        _device.BlendState = _blendOverride ?? _blend;
        _device.DepthStencilState = _stencil;
        _device.RasterizerState = RasterizerState.CullNone;

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();

            // whatever the pass set, the texture and the sampler are the ones of this batch
            _device.Textures[0] = _texture;
            _device.SamplerStates[0] = _sampler;
            if (readsLight)
                _device.SamplerStates[1] = SamplerState.LinearClamp;

            _device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _vertices, 0, _quads * 4, _indices, 0,
                _quads * 2, SpriteVertex.Declaration);
        }

        _stats.SpriteBatches++;
        _stats.Quads += _quads;
        _quads = 0;
    }

    private static readonly BlendState MultiplyBlend = new()
    {
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.Zero,
        AlphaSourceBlend = Blend.DestinationAlpha,
        AlphaDestinationBlend = Blend.Zero,
    };

    private BlendState ToBlendState(ShaderBlend blend) => blend switch
    {
        ShaderBlend.Additive => BlendState.Additive,
        ShaderBlend.Multiply => MultiplyBlend,
        ShaderBlend.Opaque => BlendState.Opaque,
        _ => _defaultBlend,
    };
}
