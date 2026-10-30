using System;
using System.Collections.Generic;
using Engine.Client.Graphics.Shaders;
using Engine.Shared.GameObjects;
using Engine.Shared.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

// A post shader is applied to the whole entity at once: its sprite and layers are first drawn into a texture of
// their own, and that texture is what goes on screen, through the shader.
public sealed partial class SpriteSystem
{
    private const int PostTextureStep = 32;

    private const int PostBorder = 1;

    private sealed class PostPass
    {
        /// <summary>
        /// The shader and what was set through SetPostShaderParam.
        /// </summary>
        public readonly SpriteTarget Target = new();

        public ProtoId<ShaderPrototype> Shader;

        public int Padding;

        /// <summary>
        /// Lower ones are applied first
        /// </summary>
        public int Order;
    }

    private sealed class PostShaderData
    {
        /// <summary>
        /// In the order they are applied.
        /// </summary>
        public readonly List<PostPass> Passes = new();

        /// <summary>
        /// Holds the entity with every pass but the last applied: the last one is applied when it goes on screen.
        /// </summary>
        public RenderTarget2D? Texture;

        // the other texture a pass draws into, reading from Texture
        public RenderTarget2D? Scratch;

        /// <summary>
        /// The part of <see cref="Texture"/> in use.
        /// </summary>
        public Rectangle Region;

        /// <summary>
        /// Where the position of the entity sits in <see cref="Region"/>.
        /// </summary>
        public Vector2 Origin;

        /// <summary>
        /// Some viewport drew it since the texture was last made, so it is worth making again.
        /// </summary>
        public bool Wanted;

        public bool Ready;
    }

    private readonly HashSet<EntityUid> _postShaded = new();

    /// <summary>
    /// Add a post shader applied after the entity is drawed
    /// </summary>
    public void SetPostShader(EntityUid uid, ProtoId<ShaderPrototype>? shader, int padding = 0)
    {
        ClearPostShaders(uid);

        if (shader is { Id: not null } id)
            AddPostShader(uid, id, padding);
    }

    /// <summary>
    /// Adds a post shader after the ones the entity already has
    /// </summary>
    public void AddPostShader(EntityUid uid, ProtoId<ShaderPrototype> shader, int padding = 0, int order = 0)
    {
        var data = GetData(uid);

        // made right away, in this same frame
        data.Post ??= new PostShaderData { Wanted = true };

        var pass = FindPass(data.Post, shader);
        if (pass is not null)
            data.Post.Passes.Remove(pass);
        else
            pass = new PostPass { Shader = shader };

        pass.Padding = Math.Max(0, padding);
        pass.Order = order;

        // after the ones with the same order
        var index = data.Post.Passes.Count;
        while (index > 0 && data.Post.Passes[index - 1].Order > order)
            index--;

        data.Post.Passes.Insert(index, pass);
        _postShaded.Add(uid);
    }

    /// <summary>
    /// Takes one post shader off the entity
    /// </summary>
    public void RemovePostShader(EntityUid uid, ProtoId<ShaderPrototype> shader)
    {
        if (!_render.TryGetValue(uid, out var data) || data.Post is not { } post || FindPass(post, shader) is not { } pass)
            return;

        ReleaseShader(pass.Target);
        post.Passes.Remove(pass);

        if (post.Passes.Count == 0)
            ReleasePost(uid, data);
    }

    /// <summary>
    /// Takes every post shader off the entity.
    /// </summary>
    public void ClearPostShaders(EntityUid uid)
    {
        if (_render.TryGetValue(uid, out var data))
            ReleasePost(uid, data);
    }

    /// <summary>
    /// Whether the sprite or one of its layers has a shader of its own. <see cref="SetDrawShader"/> replaces those,
    /// a post shader goes over them.
    /// </summary>
    public bool HasOwnShader(SpriteComponent comp)
    {
        if (comp.Shader is not null)
            return true;

        if (comp.Layers is null)
            return false;

        foreach (var layer in comp.Layers)
        {
            if (layer.Shader is not null)
                return true;
        }

        return false;
    }

    public bool TryGetPostShaderBounds(EntityUid uid, out Vector2 topLeft, out Vector2 size)
    {
        topLeft = size = default;

        if (!TryComp<SpriteComponent>(uid, out var comp) || Transform(uid) is not { } transform)
            return false;

        var data = GetData(uid);
        if (GetSprite(comp, data) is not { } spr)
            return false;

        UpdateSpriteFields(comp, transform, ref spr);
        spr.Rotation = 0f;

        if (!MeasureLocal(comp, data, transform, spr, out var min, out var max))
            return false;

        var padding = new Vector2(TotalPadding(data.Post));
        topLeft = min - padding;
        size = max - min + padding * 2f;
        return true;
    }

    // each shader draws around what the one before it drew, so the room they asked for adds up
    private static int TotalPadding(PostShaderData? post)
    {
        var padding = 0;
        if (post is null)
            return padding;

        foreach (var pass in post.Passes)
            padding += pass.Padding;

        return padding;
    }

    public bool HasPostShader(EntityUid uid, ProtoId<ShaderPrototype> shader)
        => _render.TryGetValue(uid, out var data) && data.Post is { } post && FindPass(post, shader) is not null;

    /// <summary>
    /// The post shaders of an entity, in the order they are applied.
    /// </summary>
    public IEnumerable<ProtoId<ShaderPrototype>> GetPostShaders(EntityUid uid)
    {
        if (!_render.TryGetValue(uid, out var data) || data.Post is not { } post)
            yield break;

        foreach (var pass in post.Passes)
            yield return pass.Shader;
    }

    /// <summary>
    /// Sets a parameter on one post shader of an entity.
    /// </summary>
    public void SetPostShaderParam(EntityUid uid, ProtoId<ShaderPrototype> shader, string name, Vector4 value)
    {
        if (_render.TryGetValue(uid, out var data) && data.Post is { } post && FindPass(post, shader) is { } pass)
            SetTargetParam(pass.Target, name, value);
    }

    /// <inheritdoc cref="SetPostShaderParam(EntityUid, ProtoId{ShaderPrototype}, string, Vector4)"/>
    public void SetPostShaderParam(EntityUid uid, ProtoId<ShaderPrototype> shader, string name, float value)
        => SetPostShaderParam(uid, shader, name, new Vector4(value, 0f, 0f, 0f));

    /// <inheritdoc cref="SetPostShaderParam(EntityUid, ProtoId{ShaderPrototype}, string, Vector4)"/>
    public void SetPostShaderParam(EntityUid uid, ProtoId<ShaderPrototype> shader, string name, Color value)
        => SetPostShaderParam(uid, shader, name, value.ToVector4());

    /// <summary>
    /// Sets a parameter on every post shader of an entity.
    /// </summary>
    public void SetPostShaderParam(EntityUid uid, string name, Vector4 value)
    {
        if (!_render.TryGetValue(uid, out var data) || data.Post is not { } post)
            return;

        foreach (var pass in post.Passes)
            SetTargetParam(pass.Target, name, value);
    }

    /// <inheritdoc cref="SetPostShaderParam(EntityUid, string, Vector4)"/>
    public void SetPostShaderParam(EntityUid uid, string name, float value)
        => SetPostShaderParam(uid, name, new Vector4(value, 0f, 0f, 0f));

    /// <inheritdoc cref="SetPostShaderParam(EntityUid, string, Vector4)"/>
    public void SetPostShaderParam(EntityUid uid, string name, Color value)
        => SetPostShaderParam(uid, name, value.ToVector4());

    private static PostPass? FindPass(PostShaderData post, ProtoId<ShaderPrototype> shader)
    {
        foreach (var pass in post.Passes)
        {
            if (pass.Shader == shader)
                return pass;
        }

        return null;
    }

    private void ReleasePost(EntityUid uid, SpriteRenderData data)
    {
        _postShaded.Remove(uid);

        if (data.Post is not { } post)
            return;

        foreach (var pass in post.Passes)
            ReleaseShader(pass.Target);

        ReturnPostTexture(post.Texture);
        ReturnPostTexture(post.Scratch);
        data.Post = null;
    }

    // Puts the image of the entity in the queue. False when there is none yet, and it has to be drawn as usual.
    private bool SubmitPost(SpriteComponent comp, SpriteRenderData data, PostShaderData post, TransformComponent transform, Vector2 pos)
    {
        post.Wanted = true;

        if (!post.Ready || post.Texture is null || post.Passes.Count == 0)
            return false;

        var last = post.Passes[^1];
        var shader = ResolveShader(last.Target, last.Shader);

        var image = new RawTexture(post.Texture, post.Region, Vector2.Zero, post.Origin,
            transform.Angle + data.DrawRotation, Vector2.One, Color.White, data.DrawDepth ?? comp.Depth)
        {
            Layer = comp.Layer,
            SamplerState = comp.SamplerState,

            // the layers were drawn into the image unlit, the whole image takes the light the sprite would
            Lighting = GetLighting(comp, null, transform),
        };

        _renderMan.Submit(image, pos, shader, last.Target.Custom0, last.Target.Custom1);
        return true;
    }

    // before any viewport is drawn
    private void DrawPostTextures()
    {
        if (_postShaded.Count == 0)
            return;

        var renderer = (RenderManager)_renderMan;
        var device = GameClient.GraphicsDevice;

        foreach (var uid in _postShaded)
        {
            if (!_render.TryGetValue(uid, out var data) || data.Post is not { Wanted: true } post)
                continue;

            post.Wanted = false;

            if (!TryComp<SpriteComponent>(uid, out var comp) || Transform(uid) is not { } transform
                || GetSprite(comp, data) is not { } spr)
                continue;

            // the image is the entity unrotated, the rotation is applied to the image itself
            UpdateSpriteFields(comp, transform, ref spr);
            spr.Rotation = 0f;

            if (!MeasureLocal(comp, data, transform, spr, out var min, out var max))
                continue;

            var padding = TotalPadding(post);

            var width = (int)MathF.Ceiling(max.X - min.X) + padding * 2;
            var height = (int)MathF.Ceiling(max.Y - min.Y) + padding * 2;

            var needed = new Point(width + PostBorder * 2, height + PostBorder * 2);
            if (post.Texture is not { IsDisposed: false } texture || texture.Width < needed.X || texture.Height < needed.Y)
            {
                ReturnPostTexture(post.Texture);
                post.Texture = texture = RentPostTexture(device, RoundUp(needed.X), RoundUp(needed.Y));
            }

            post.Region = new Rectangle(PostBorder, PostBorder, width, height);
            post.Origin = new Vector2(padding - min.X, padding - min.Y);

            renderer.RenderToTarget(texture,
                Matrix.CreateTranslation(post.Origin.X + PostBorder, post.Origin.Y + PostBorder, 0f),
                (System: this, comp, data, transform, spr),
                static s => s.System.SubmitWithLayers(s.comp, s.data, s.transform, s.spr, Vector2.Zero, local: true));

            // every pass but the last, the image goes through its shader into the other texture
            for (var i = 0; i < post.Passes.Count - 1; i++)
            {
                var pass = post.Passes[i];
                if (ResolveShader(pass.Target, pass.Shader) is not { } shader)
                    continue;

                if (post.Scratch is not { IsDisposed: false } scratch
                    || scratch.Width != post.Texture.Width || scratch.Height != post.Texture.Height)
                {
                    ReturnPostTexture(post.Scratch);
                    post.Scratch = scratch = RentPostTexture(device, post.Texture.Width, post.Texture.Height);
                }

                var image = new RawTexture(post.Texture, post.Region, Vector2.Zero, Vector2.Zero, 0f, Vector2.One, Color.White, 0f)
                {
                    SamplerState = comp.SamplerState,
                };

                renderer.RenderToTarget(scratch, Matrix.Identity,
                    (renderer, image, shader, pass.Target.Custom0, pass.Target.Custom1),
                    static s => s.renderer.Submit(s.image, new Vector2(PostBorder), s.shader, s.Custom0, s.Custom1));

                (post.Texture, post.Scratch) = (scratch, post.Texture);
            }

            post.Ready = true;
        }
    }

    // Textures of entities that lost their post shaders, kept to be given to the next one
    private const int PostTexturePoolSize = 16;
    private readonly List<RenderTarget2D> _postTexturePool = new();

    private RenderTarget2D RentPostTexture(GraphicsDevice device, int width, int height)
    {
        for (var i = _postTexturePool.Count - 1; i >= 0; i--)
        {
            var texture = _postTexturePool[i];
            if (texture.IsDisposed)
            {
                _postTexturePool.RemoveAt(i);
                continue;
            }

            if (texture.Width != width || texture.Height != height)
                continue;

            _postTexturePool.RemoveAt(i);
            return texture;
        }

        return new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
    }

    private void ReturnPostTexture(RenderTarget2D? texture)
    {
        if (texture is null || texture.IsDisposed)
            return;

        // the oldest goes when it is full
        if (_postTexturePool.Count >= PostTexturePoolSize)
        {
            _postTexturePool[0].Dispose();
            _postTexturePool.RemoveAt(0);
        }

        _postTexturePool.Add(texture);
    }

    private static int RoundUp(int size)
        => (size + PostTextureStep - 1) / PostTextureStep * PostTextureStep;

    private bool MeasureLocal(SpriteComponent comp, SpriteRenderData data, TransformComponent transform, Sprite2D spr,
        out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.MaxValue);
        max = new Vector2(float.MinValue);

        if (!data.HideBaseSprite)
            Grow(spr, Vector2.Zero, ref min, ref max);

        if (comp.Layers is not null)
        {
            foreach (var layer in comp.Layers)
            {
                if (!layer.Visible || GetLayerSprite(layer, comp, GetLayerTarget(data, layer.Id)) is not { } piece)
                    continue;

                UpdateLayerFields(layer, transform, comp, ref piece);
                Grow(piece, layer.Offset, ref min, ref max);
            }
        }

        if (min.X > max.X || min.Y > max.Y)
            return false;

        min = new Vector2(MathF.Floor(min.X), MathF.Floor(min.Y));
        max = new Vector2(MathF.Ceiling(max.X), MathF.Ceiling(max.Y));
        return true;
    }

    private static void Grow(Sprite2D sprite, Vector2 at, ref Vector2 min, ref Vector2 max)
    {
        var a = at - sprite.Origin * sprite.Scale;
        var b = a + new Vector2(sprite.Width, sprite.Height) * sprite.Scale;

        // a negative scale flips the two corners
        min = Vector2.Min(min, Vector2.Min(a, b));
        max = Vector2.Max(max, Vector2.Max(a, b));
    }
}
