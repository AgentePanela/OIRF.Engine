namespace Engine.Shared.GameObjects;

/// <summary>
/// This component is used to "attach" the game's camera to a entity position.
/// Only one active Camera2DComponent is allowed.
/// </summary>
[RegisterComponent("Camera2D")]
public sealed class Camera2DComponent : Component
{
    public bool Active { get; set; } = true;
    public float MinimumZoom { get; set; } = 0.1f;
    public float MaximumZoom { get; set; } = float.MaxValue;
    public float? InitialZoom { get; set; }

    /// <summary>
    /// If true it will set the camera position to the entity transform comp position.
    /// </summary>
    public bool UsePosition { get; set; } = true;

    /// <summary>
    /// If true, the cam rotation will turn to the entity transform comp angle.
    /// </summary>
    public bool UseAngle { get; set; } = true;
}
