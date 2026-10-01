using System.Collections.Generic;
using Engine.Shared.Assets;
using Engine.Shared.Prototypes;

namespace Engine.Shared.Audio;

/// <summary>
/// A set of sounds to pick one from at random every time it plays, through a
/// <see cref="SoundCollectionSpecifier"/>.
/// </summary>
[Prototype("soundCollection")]
public sealed class SoundCollectionPrototype : IPrototype
{
    [DataField("type", required: true)]
    public string Type { get; set; }

    [DataField("id", required: true)]
    public string ID { get; set; }

    /// <summary>
    /// Audio keys, e.g. "Player/Walking/walk".
    /// </summary>
    [AudioKey]
    [DataField("files", required: true)]
    public List<string> Files { get; set; } = new();
}
