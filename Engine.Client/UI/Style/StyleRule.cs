using System.Collections.Generic;
using Engine.Shared.Prototypes;

namespace Engine.Client.UI;

public sealed class StyleClass
{
    [DataField("control")] public string? ControlType { get; set; }
    [DataField("styleClass")] public string? Class { get; set; }
    [DataField("pseudoClass")] public string? PseudoClass { get; set; }
    [DataField("id")] public string? Identifier { get; set; }

    public bool Matches(Control c)
    {
        if (ControlType is not null && c.GetType().Name != ControlType)
            return false;

        if (Class is not null && !c.StyleClasses.Contains(Class))
            return false;

        if (PseudoClass is not null && !c.PseudoClasses.Contains(PseudoClass))
            return false;

        if (Identifier is not null && c.StyleIdentifier != Identifier)
            return false;

        return true;
    }

    public int Specificity { get; set; }
}

public sealed class StyleRule
{
    [DataField("class", required: true)]
    public StyleClass StyleClass { get; set; } = default!;

    [DataField("properties", required: true)]
    public Dictionary<string, object> Properties { get; set; } = new();
}