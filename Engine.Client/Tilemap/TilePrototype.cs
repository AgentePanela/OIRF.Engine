using Engine.Shared.Prototypes;

namespace Engine.Client.Tilemap;

[Prototype("tile")]
public sealed class TilePrototype : IPrototype
{
    [DataField("type", required: true)]
    public string Type { get; private set; }

    [DataField("id", required: true)]
    public string ID { get; private set; }

    [DataField("sprite")]
    public string Sprite { get; private set; }

    /// <summary>
    /// Determines blending order. Higher priority tiles blend on top of lower priority neighbors.
    /// </summary>
    [DataField("blendPriority")]
    public int BlendPriority { get; private set; } = 0;

    /// <summary>
    /// If true, entities with physics cannot walk through this tile
    /// </summary>
    [DataField("solid")]
    public bool Solid { get; private set; } = false;
}
