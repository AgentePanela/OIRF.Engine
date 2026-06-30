using Engine.Shared.Prototypes;

namespace Engine.Shared.Tags;

/// <summary>
/// Define a tag id. ID is really the only thing u need to fill up lol
/// </summary>
[Prototype("tag")]
public sealed class TagPrototype : IPrototype
{
    [DataField("type", required: true)]
    public string Type { get; set; }

    [DataField("id", required: true)]
    public string ID { get; set; } 
}
