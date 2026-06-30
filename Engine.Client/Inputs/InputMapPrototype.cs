using Engine.Shared.Prototypes;
using System;
using System.Collections.Generic;

namespace Engine.Client.Inputs;

/// <summary>
/// Prototype that defines named input actions with keyboard, mouse, and gamepad bindings.
/// </summary>
[Prototype("inputMap", loadPriority: -10)]
public sealed class InputMapPrototype : IPrototype
{
    [DataField("type", required: true)]
    public string Type { get; private set; }

    [DataField("id", required: true)]
    public string ID { get; private set; }

    /// <summary>
    /// Named actions mapped to their bindings.
    /// Key = action name (e.g. "MoveUp"), Value = binding definition.
    /// </summary>
    [DataField("actions", required: true)]
    public Dictionary<string, InputAction> Actions { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Defines the input bindings for a single action.
/// Each list entry is the enum name of the corresponding input type.
/// </summary>
public sealed class InputAction
{
    /// <summary>
    /// Keyboard key names (e.g. "W", "Up", "Space"). Parsed as <see cref="Microsoft.Xna.Framework.Input.Keys"/>.
    /// </summary>
    [DataField("keys")]
    public List<string> Keys { get; set; } = new();

    /// <summary>
    /// Mouse button names (e.g. "Left", "Right", "Middle"). Parsed as <see cref="Engine.Inputs.MouseButton"/>.
    /// </summary>
    [DataField("mouse")]
    public List<string> Mouse { get; set; } = new();

    /// <summary>
    /// Gamepad button names (e.g. "A", "X", "LeftShoulder"). Parsed as <see cref="Microsoft.Xna.Framework.Input.Buttons"/>.
    /// </summary>
    [DataField("gamepad")]
    public List<string> Gamepad { get; set; } = new();
}
