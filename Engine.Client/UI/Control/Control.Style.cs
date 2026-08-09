using System.Collections.Generic;

namespace Engine.Client.UI;

public abstract partial class Control
{
    #region Styles

    public HashSet<string> StyleClasses { get; } = new();

    public HashSet<string> PseudoClasses { get; private set; } = new();

    /// <summary>
    /// A unique style identifier, similar to #id in CSS.
    /// </summary>
    public string? StyleIdentifier { get; set; }

    private StylePrototype? _stylesheetOverride;
    public StylePrototype? StylesheetOverride
    {
        get => _stylesheetOverride;
        set
        {
            if (_stylesheetOverride == value)
                return;
            _stylesheetOverride = value;
            OnThemeUpdated();
        }
    }

    /// <summary>
    /// Called when this control theme in the control tree changed.
    /// </summary>
    protected virtual void OnThemeUpdated()
    {

    }
    
    /// <summary>
    /// Resolves a style property for this control and
    /// returns the value from whichever matching rule has the highest <see cref="StyleClass.Specificity"/>.
    /// </summary>
    public bool TryGetStyleProperty<T>(string name, out T? value)
    {
        var sheet = FindEffectiveStylesheet();
        if (sheet is null)
        {
            value = default;
            return false;
        }

        StyleRule? best = null;

        foreach (var rule in sheet.Rules)
        {
            if (!rule.Properties.ContainsKey(name))
                continue;

            if (!rule.StyleClass.Matches(this))
                continue;

            if (best is null || rule.StyleClass.Specificity > best.StyleClass.Specificity)
                best = rule;
        }

        if (best is null || best.Properties[name] is not T typed)
        {
            value = default;
            return false;
        }

        value = typed;
        return true;
    }

    /// <summary>
    /// Walks up from this control (inclusive) to the nearest ancestor with a
    /// <see cref="StylesheetOverride"/> set.
    /// </summary>
    private StylePrototype? FindEffectiveStylesheet()
    {
        for (var control = this; control is not null; control = control.Parent)
        {
            if (control.StylesheetOverride is not null)
                return control.StylesheetOverride;
        }

        return null;
    }

    #endregion
}
