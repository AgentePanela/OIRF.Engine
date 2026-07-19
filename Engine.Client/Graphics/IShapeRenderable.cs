namespace Engine.Client.Graphics;

/// <summary>
/// An <see cref="IRenderable"/> drawn through <see cref="GameClient.ShapeBatch"/>
/// tells shapes apart from sprites/text with an `is IShapeRenderable` check.
/// </summary>
public interface IShapeRenderable : IRenderable
{
    /// <summary>
    /// Shapes cannot hold effects. This bool is created to set if a shader should be unshaded (ignore light)
    /// </summary>
    bool Unshaded { get; set; }
}
