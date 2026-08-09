using System.Collections.Generic;
using Engine.Shared.Prototypes;

namespace Engine.Client.UI;

// TODO: multiple classes per selector (styleClass: [danger, large], requiring all at once)
// TODO: and ancestor combinators (descendant/child, like CSS ".dialog Button" / ".dialog > Button")

// todo: automatic inheritance of property
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

    /// <summary>
    /// How specific this selector is. Used to pick a winner when multiple rules match the
    /// same control, same order as CSS: id > pseudo-class/style-class > control type.
    /// </summary>
    public int Specificity =>
        (Identifier is not null ? 100 : 0) +
        (Class is not null ? 5 : 0) +
        (PseudoClass is not null ? 5 : 0) +
        (ControlType is not null ? 10 : 0);
}

public sealed class StyleRule
{
    [DataField("class", required: true)]
    public StyleClass StyleClass { get; set; } = default!;

    [DataField("properties", required: true)]
    public Dictionary<string, object> Properties { get; set; } = new();
}