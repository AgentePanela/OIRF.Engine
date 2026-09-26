using Microsoft.Xna.Framework.Graphics;

namespace Engine.Shared.Graphics;

/// <summary>
/// The sampler states a sprite can use. A <see cref="SamplerState"/> is a GPU state object and does not go over the
/// wire, so a sprite's sampler replicates as its index in here.
/// </summary>
public static class SamplerPresets
{
    /// <summary>
    /// Index 0 is "no sampler", the renderer's default.
    /// </summary>
    public static readonly (string Name, SamplerState? Value)[] All =
    [
        ("None", null),
        ("PointClamp", SamplerState.PointClamp),
        ("PointWrap", SamplerState.PointWrap),
        ("LinearClamp", SamplerState.LinearClamp),
        ("LinearWrap", SamplerState.LinearWrap),
        ("AnisotropicClamp", SamplerState.AnisotropicClamp),
        ("AnisotropicWrap", SamplerState.AnisotropicWrap),
    ];

    private static bool _warnedCustom;

    public static byte IndexOf(SamplerState? state)
    {
        for (var i = 0; i < All.Length; i++)
        {
            if (ReferenceEquals(All[i].Value, state))
                return (byte)i;
        }

        if (!_warnedCustom)
        {
            _warnedCustom = true;
            Log.Warn("A sprite uses a sampler state that is not one of the presets, forcing default.");
        }

        return 0;
    }

    public static SamplerState? Get(byte index)
        => index < All.Length ? All[index].Value : null;
}
