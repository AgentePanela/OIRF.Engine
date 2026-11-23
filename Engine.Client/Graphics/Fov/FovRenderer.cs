namespace Engine.Client.Graphics;

/// <summary>
/// How a viewport works out what its eye sees and draws what it does not.
/// </summary>
public abstract class FovRenderer
{
    /// <summary>
    /// Fills the mask of this frame, before the world is drawn.
    /// </summary>
    public abstract void BuildMask(in FovMaskContext ctx);

    /// <summary>
    /// Draws what is out of sight, over the lit world and before bloom.
    /// </summary>
    public abstract void Compose(in FovComposeContext ctx);
}

/// <summary>
/// Engine default FoV.
/// </summary>
public class ShadowFov : FovRenderer
{
    public override void BuildMask(in FovMaskContext ctx)
        => ctx.DrawShadows(ctx.EyePosition);

    public override void Compose(in FovComposeContext ctx)
    {
        if (ctx.DefaultShader is { } shader)
            ctx.DrawOver(shader);
    }
}
