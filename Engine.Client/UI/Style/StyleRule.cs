using System;
using System.Collections.Generic;
using System.Linq;
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

    private bool IsUniversal => string.IsNullOrEmpty(Class) || Class == "*";

    public bool Matches(Control c)
    {
        if (!MatchesControlType(c) || !MatchesPseudoClass(c))
            return false;

        // Empty class ("") or "*" matches everything (universal selector)
        if (!IsUniversal && (Class is null || !c.StyleClasses.Contains(Class)))
            return false;

        if (Identifier is not null && c.StyleIdentifier != Identifier)
            return false;

        return true;
    }

    private bool MatchesControlType(Control c)
    {
        if (ControlType is null)
            return true;

        // walk the class hierarchy - a rule targeting "Label" should also match a Label subclass
        for (var type = c.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            if (string.Equals(type.Name, ControlType, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return c.StyleAliasses.Any(a => string.Equals(a, ControlType, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesPseudoClass(Control c)
        => PseudoClass is null || c.PseudoClasses.Any(p => string.Equals(p, PseudoClass, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// How specific this selector is. Used to pick a winner when multiple rules match the
    /// same control, same order as CSS: id > pseudo-class/style-class > control type > universal.
    /// Universal selector ("" or "*") has the lowest specificity.
    /// </summary>
    public int Specificity =>
        (Identifier is not null ? 100 : 0) +
        (Class is not null && !IsUniversal ? 5 : 0) +
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