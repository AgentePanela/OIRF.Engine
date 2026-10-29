using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ShadowVertex : IVertexType
{
    public Vector2 Position;
    public Vector4 Segment;
    public Vector4 Light;
    public Vector4 Slot;

    /// <summary>
    /// The light that goes through the edge (rgb)
    /// </summary>
    public Vector4 Transmit;

    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
        new VertexElement(8, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(24, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 1),
        new VertexElement(40, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 2),
        new VertexElement(56, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 3));

    readonly VertexDeclaration IVertexType.VertexDeclaration => Declaration;
}

/// <summary>
/// The shadows of every light in an atlas page, sent in as few draw calls as the 16 bit indices allow.
/// </summary>
internal sealed class ShadowBatch
{
    // 4 vertices each, and indices are 16 bit
    private const int MaxQuads = 16384;

    private readonly short[] _indices = new short[MaxQuads * 6];
    private ShadowVertex[] _vertices = new ShadowVertex[256 * 4];
    private int _quads;

    /// <summary>
    /// Edges sent since the stats were last read.
    /// </summary>
    public int Count { get; set; }

    // the size of what is drawn into, see SetTarget
    private Vector2 _targetSize = new(LightAtlas.PageSize);
    private bool _seeThrough;

    // the light drawn in the page times what goes through the edge, only where its shadow is
    private static readonly BlendState SeeThroughBlend = new()
    {
        ColorWriteChannels = ColorWriteChannels.Red | ColorWriteChannels.Green | ColorWriteChannels.Blue,
        ColorBlendFunction = BlendFunction.Add,
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.Zero,
    };

    private static readonly BlendState ShadowBlend = new()
    {
        // only the alpha of the page
        ColorWriteChannels = ColorWriteChannels.Alpha,
        AlphaBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.One,
    };

    public ShadowBatch()
    {
        for (var i = 0; i < MaxQuads; i++)
        {
            var v = i * 4;
            var n = i * 6;

            // near a, near b, far a / near b, far b, far a
            _indices[n] = (short)v;
            _indices[n + 1] = (short)(v + 1);
            _indices[n + 2] = (short)(v + 2);
            _indices[n + 3] = (short)(v + 1);
            _indices[n + 4] = (short)(v + 3);
            _indices[n + 5] = (short)(v + 2);
        }
    }

    /// <summary>
    /// Queues the shadow <paramref name="segment"/> throws from <paramref name="light"/>, which has its square.
    /// </summary>
    public void Add(GraphicsDevice device, Effect effect, ShadowSegment segment, in LightDraw light)
    {
        var slot = new Vector4(light.Region.Center.ToVector2(), light.SlotScale, light.Region.Width / 2f);
        Add(device, effect, segment, light.Center, light.Radius, light.SourceRadius, slot);
    }

    /// <summary>
    /// Queues the shadow <paramref name="segment"/> throws from a light at <paramref name="center"/>.
    /// </summary>
    public void Add(GraphicsDevice device, Effect effect, ShadowSegment segment, Vector2 center, float radius,
        float source, Vector4 slot)
    {
        if (_quads == MaxQuads)
            Draw(device, effect);

        var a = segment.A;
        var b = segment.B;

        var outA = OuterDirection(a, b, center, source);
        var outB = OuterDirection(b, a, center, source);

        // far enough to leave the light circle from anywhere inside it
        var halfOpening = MathF.Sqrt(MathF.Max((1f + Vector2.Dot(outA, outB)) / 2f, 0f));
        var reach = radius * 2f / MathF.Max(halfOpening, 0.05f);
        var farA = a + outA * reach;
        var farB = b + outB * reach;

        var segmentData = new Vector4(a, b.X, b.Y);
        var lightData = new Vector4(center, source, 0f);
        var transmit = new Vector4(segment.Transmit, 0f);

        var index = _quads * 4;
        if (index + 4 > _vertices.Length)
            Array.Resize(ref _vertices, Math.Min(_vertices.Length * 2, MaxQuads * 4));

        _vertices[index] = new ShadowVertex { Position = a, Segment = segmentData, Light = lightData, Slot = slot, Transmit = transmit };
        _vertices[index + 1] = new ShadowVertex { Position = b, Segment = segmentData, Light = lightData, Slot = slot, Transmit = transmit };
        _vertices[index + 2] = new ShadowVertex { Position = farA, Segment = segmentData, Light = lightData, Slot = slot, Transmit = transmit };
        _vertices[index + 3] = new ShadowVertex { Position = farB, Segment = segmentData, Light = lightData, Slot = slot, Transmit = transmit };

        _quads++;
        Count++;
    }

    /// <summary>
    /// Makes the shadows queued from now on go to a target <paramref name="size"/> pixels big instead of an atlas
    /// page, until the next <see cref="Flush"/>.
    /// </summary>
    public void SetTarget(Vector2 size)
        => _targetSize = size;

    /// <summary>
    /// Makes the edges queued from now on, of see-through occluders, tint the light already drawn in the page.
    /// </summary>
    public void SetSeeThrough()
        => _seeThrough = true;

    /// <summary>
    /// Draws what is queued into the page that is set.
    /// </summary>
    public void Flush(GraphicsDevice device, Effect effect)
    {
        Draw(device, effect);
        _targetSize = new Vector2(LightAtlas.PageSize);
        _seeThrough = false;
    }

    private void Draw(GraphicsDevice device, Effect effect)
    {
        if (_quads == 0)
            return;

        device.BlendState = _seeThrough ? SeeThroughBlend : ShadowBlend;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;

        effect.CurrentTechnique = effect.Techniques[_seeThrough ? "SeeThrough" : "Shadow"];
        effect.Parameters["PageSize"]?.SetValue(_targetSize);

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _vertices, 0, _quads * 4, _indices, 0,
                _quads * 2, ShadowVertex.Declaration);
        }

        _quads = 0;
    }

    /// <summary>
    /// The outer side of the soft edge at <paramref name="end"/>.
    /// </summary>
    private static Vector2 OuterDirection(Vector2 end, Vector2 other, Vector2 center, float source)
    {
        var fromLight = end - center;
        var distance = fromLight.Length();
        if (distance < 0.001f)
            return Vector2.Normalize(end - other);

        if (source <= 0f)
            return fromLight / distance;

        var side = new Vector2(-fromLight.Y, fromLight.X) / distance;
        if (Vector2.Dot(side, end - other) < 0f)
            side = -side;

        var far = center - side * MathF.Min(source, distance * 0.9f);
        return Vector2.Normalize(end - far);
    }
}
