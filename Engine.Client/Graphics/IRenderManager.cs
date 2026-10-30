using System.Collections.Generic;
using Apos.Shapes;
using Engine.Client.Assets.Atlas;
using Engine.Client.Graphics.Shaders;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// How the main viewport follows the window.
/// </summary>
public enum MainViewportScaling
{
    /// <summary>
    /// The game resolution scaled to fit the window (letterboxed or cropped).
    /// </summary>
    Scaled,

    /// <summary>
    /// One world unit per window pixel, theres no resizing.
    /// </summary>
    Native,

    /// <summary>
    /// Leaves the main viewport alone, whoever set this resizes it.
    /// </summary>
    Manual,
}

/// <summary>
/// Manage all 2D rendering features in the engine/game.
/// </summary>
public interface IRenderManager
{
    BlendState BlendState { get; set; }
    SamplerState DefaultSampler { get; }

    /// <summary>
    /// The viewport the game is seen through. <see cref="Camera2D"/> from IoC is its camera.
    /// </summary>
    RenderViewport MainViewport { get; }

    IReadOnlyList<RenderViewport> Viewports { get; }

    MainViewportScaling MainViewportScaling { get; set; }

    /// <summary>
    /// When false the main viewport is drawn into its target but not put on the window, and the engine UI is not
    /// drawn either.
    /// </summary>
    bool PresentMainViewport { get; set; }

    /// <summary>
    /// The numbers of the last frame that finished drawing.
    /// </summary>
    RenderStats Stats { get; }

    /// <summary>
    /// Creates another viewport, drawn every frame before the main one while it is enabled.
    /// </summary>
    RenderViewport CreateViewport(string name, Vector2 size, float renderScale = 1f);

    /// <summary>
    /// Stops drawing a viewport made by <see cref="CreateViewport"/> and frees its target.
    /// </summary>
    void RemoveViewport(RenderViewport viewport);

    /// <summary>
    /// Every overlay added, in the order they draw within a stage.
    /// </summary>
    IReadOnlyList<RenderOverlay> Overlays { get; }

    /// <summary>
    /// Starts drawing an overlay, in every viewport it applies to.
    /// </summary>
    void AddOverlay(RenderOverlay overlay);

    void RemoveOverlay(RenderOverlay overlay);

    /// <summary>
    /// Everything submitted until the returned scope is disposed writes to, or is cut by, the mask <paramref name="id"/>
    /// (1 to 255) of the viewport being drawn.
    /// <b>USE WITH "using BeginMask"</b>
    /// </summary>
    MaskScope BeginMask(MaskMode mode, int id = 1);

    /// <summary>
    /// For draw systems to tell the profiler how much they skipped for being off screen.
    /// </summary>
    void ReportCulled(int count = 1);

    /// <summary>
    /// Traslate a window position to a world position through the main viewport.
    /// </summary>
    Vector2 ScreenToWorld(Vector2 screenPos);

    #region Queue

    /// <summary>
    /// Queues a sprite. <paramref name="custom0"/> and <paramref name="custom1"/> is the shaders custom parameters.
    /// </summary>
    void Submit(Sprite2D sprite, Vector2 position, ShaderInstance? shader, Vector4 custom0, Vector4 custom1 = default);

    /// <inheritdoc cref="Submit(Sprite2D, Vector2, ShaderInstance?, Vector4, Vector4)"/>
    void Submit(Sprite2D sprite, Vector2 position, ShaderInstance? shader = null);

    /// <summary>
    /// Queues a raw texture. <paramref name="custom0"/> and <paramref name="custom1"/> is the shaders custom parameters.
    /// </summary>
    void Submit(RawTexture texture, Vector2 position, ShaderInstance? shader, Vector4 custom0, Vector4 custom1 = default);

    /// <inheritdoc cref="Submit(RawTexture, Vector2, ShaderInstance?, Vector4, Vector4)"/>
    void Submit(RawTexture texture, Vector2 position, ShaderInstance? shader = null);

    /// <summary>
    /// Add a IRenderable to be draw queued.
    /// </summary>
    void Submit(IRenderable renderable, Vector2 position, ShaderInstance? shader = null);

    /// <inheritdoc cref="Submit(IRenderable, Vector2, ShaderInstance?)"/>
    void Submit<T>(T renderable, Vector2 position, ShaderInstance? shader = null) where T : struct, IRenderable;

    /// <inheritdoc cref="Submit(IRenderable, Vector2, ShaderInstance?)"/>
    void Submit(RenderQueue queue);

    /// <summary>
    /// Add an <see cref="IShapeRenderable"/> to be draw queued.
    /// </summary>
    void SubmitShape<T>(T renderable, Vector2 position) where T : struct, IShapeRenderable;

    #endregion

    #region Batches

    /// <summary>
    /// Prepares and begins a new Apos.Shapes batch for shapes.
    /// </summary>
    void BeginShapes(SamplerState? samplerState = null, DepthStencilState? depthStencilState = null);

    /// <summary>
    /// Ends the batch started by <see cref="BeginShapes"/>.
    /// </summary>
    void EndShapes();

    #endregion

    #region Shapes (queued)

    void DrawRect(Rectangle rect, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0, CornerRadii rounded = default, float rotation = 0f, bool unshaded = false);
    void DrawCircle(Vector2 center, float radius, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0, float rotation = 0f, bool unshaded = false);
    void DrawEllipse(Vector2 center, float radiusX, float radiusY, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0, float rotation = 0f, bool unshaded = false);
    void DrawLine(Vector2 from, Vector2 to, float radius, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0f, bool unshaded = false);
    void DrawHexagon(Vector2 center, float radius, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0, float rounded = 0f, float rotation = 0f, bool unshaded = false);
    void DrawEquilateralTriangle(Vector2 center, float radius, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0, float rounded = 0f, float rotation = 0f, bool unshaded = false);
    void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0, float rounded = 0f, bool unshaded = false);
    void DrawArc(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0, bool unshaded = false);
    void DrawRing(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0, bool unshaded = false);
    void DrawPolygon(Vector2[] worldVerts, Gradient? borderColor = null, float thickness = 1, bool unshaded = false);

    #endregion

    #region Immediate

    // Straight into the open batch, for an IRenderable drawing itself.

    void FillRectImmediate(Rectangle rect, Gradient color);

    void DrawRectShape(RectRenderable r);
    void DrawCircleShape(CircleRenderable r);
    void DrawEllipseShape(EllipseRenderable r);
    void DrawLineShape(LineRenderable r);
    void DrawHexagonShape(HexagonRenderable r);
    void DrawEquilateralTriangleShape(EquilateralTriangleRenderable r);
    void DrawTriangleShape(TriangleRenderable r);
    void DrawArcShape(ArcRenderable r);
    void DrawRingShape(RingRenderable r);

    /// <summary>
    /// Draw a raw atlas page directly.
    /// </summary>
    void DrawRaw(AtlasPage page, AtlasSprite sprite, Vector2 position,
        Color color = default, float rotation = 0f, Vector2 origin = default, Vector2 scale = default, SpriteEffects effects = SpriteEffects.None,
        float layerDepth = 0f);

    /// <summary>
    /// Draw a standalone Texture2D directly (NOT A "RAW TEXTURE").
    /// </summary>
    void DrawRawTexture2D(Texture2D texture, Vector2 position,
        Color color = default, float rotation = 0f, Vector2 origin = default, Vector2 scale = default, SpriteEffects effects = SpriteEffects.None,
        float layerDepth = 0f);

    /// <summary>
    /// A region of a texture with its top left at <paramref name="position"/>, nothing else applied.
    /// </summary>
    void DrawQuad(Texture2D texture, Rectangle source, Vector2 position, Color color);

    void DrawSprite(Sprite2D sprite, Vector2 position);
    void DrawTexture(RawTexture texture, Vector2 position);
    void DrawNineSlice(NineSlice2D slice, Vector2 position);
    void DrawString(Label2D label, Vector2 position);

    #endregion
}
