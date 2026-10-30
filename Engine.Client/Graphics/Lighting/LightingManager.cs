using System;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Runtime configuration for the lighting pipeline.
/// </summary>
public sealed class LightingManager
{
    public bool Enabled { get; private set; } = false;

    /// <summary>
    /// Ambient color used when the scene has no AmbientLightComponent.
    /// </summary>
    public Color AmbientLight { get; set; } = new Color(0, 0, 0);

    /// <summary>
    /// Global multiplier applied on top of every light's intensity.
    /// </summary>
    public float LightIntensity { get; set; } = 1f;

    /// <summary>
    /// Fired when Enabled changes.
    /// </summary>
    public event Action<bool>? OnEnabledChanged;

    public void SetEnabled(bool value)
    {
        if (Enabled == value) return;
        Enabled = value;
        OnEnabledChanged?.Invoke(value);
    }

    /// <summary>
    /// Override the default ambient light for the current scene.
    /// </summary>
    public void SetAmbient(Color color, float intensity = 1f)
    {
        AmbientLight = color * intensity;
    }
}
