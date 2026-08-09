using System.Collections.Generic;
using Engine.Shared.Prototypes;

namespace Engine.Client.UI;

/// <summary>
/// A ui style sheet. Controls how a widget will be 
/// </summary>
[Prototype("style")]
public sealed class StylePrototype : IPrototype, IInheritingPrototype
{
    public string Type => "style";

    [DataField("id", required: true)]
    public string ID { get; set; } = "";

    [DataField("parent")]
    public string[]? Parents { get; set; }

    [DataField("abstract")]
    public bool Abstract { get; set; }

    [DataField("rules")]
    public List<StyleRule> Rules = new();
}