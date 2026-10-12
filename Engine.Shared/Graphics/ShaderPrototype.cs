using System.Collections.Generic;
using Engine.Shared.Prototypes;

namespace Engine.Shared.Graphics;

/// <summary>
/// How what a shader draws is mixed with what is already there.
/// </summary>
public enum ShaderBlend
{
    /// <summary>
    /// Regular transparency.
    /// </summary>
    Alpha,

    /// <summary>
    /// Adds to what is there, for glows and light.
    /// </summary>
    Additive,

    /// <summary>
    /// Multiplies what is there, darkening it.
    /// </summary>
    Multiply,

    /// <summary>
    /// Replaces what is there, alpha is ignored.
    /// </summary>
    Opaque,
}

/// <summary>
/// A prototype thats tell the renderer how a shader should act.
/// </summary>
[Prototype("shader")]
public sealed class ShaderPrototype : IPrototype
{
    public string Type => "shader";

    [DataField("id", required: true)]
    public string ID { get; set; } = "";

    /// <summary>
    /// File name of the .fx under Shaders/, without extension. Uses the proto id when null.
    /// </summary>
    [DataField("path")]
    public string? Path { get; set; }

    /// <summary>
    /// False for what glows on its own and ignores the lighting (unshaded);
    /// </summary>
    [DataField("lit")]
    public bool Lit { get; set; } = true;

    [DataField("blend")]
    public ShaderBlend Blend { get; set; } = ShaderBlend.Alpha;

    /// <summary>
    /// Starting values for the shader's own parameters: a number, or a list of up to 4 numbers for a vector.
    /// </summary>
    [DataField("params")]
    public Dictionary<string, object>? Params { get; set; }

    /// <summary>
    /// Parameters that are different for each sprite and travel with it instead of being set on the shader, so
    /// sprites with different values still draw together.
    /// </summary>
    [DataField("perSprite")]
    public Dictionary<string, string>? PerSprite { get; set; }

    public string File => Path ?? ID;
}
