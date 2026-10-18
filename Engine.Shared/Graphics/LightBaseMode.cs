namespace Engine.Shared.Graphics;

/// <summary>
/// How a sprite reads the light it is drawn with.
/// </summary>
public enum LightBaseMode : byte
{
    /// <summary>
    /// Every pixel reads the light under itself.
    /// </summary>
    Pixel,

    /// <summary>
    /// Every pixel reads the light on the base line, straight below or above itself.
    /// </summary>
    Line,

    /// <summary>
    /// The whole sprite reads the light of the base point.
    /// </summary>
    Point,
}
