using Engine.Shared.Prototypes;

namespace Engine.Shared.Audio;

/// <summary>
/// Declares a valid audio tag id. Sounds can carry any number of tags (AudioComponent.Tags),
/// and each tag gets its own runtime-adjustable volume multiplier.
/// </summary>
[Prototype("audioTag")]
public sealed class AudioTagPrototype : IPrototype
{
    [DataField("type", required: true)]
    public string Type { get; set; }

    [DataField("id", required: true)]
    public string ID { get; set; }
}
