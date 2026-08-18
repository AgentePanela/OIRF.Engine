using System.Collections.Generic;
using Engine.Shared.IoC;
using Engine.Shared.Prototypes;

namespace Engine.Client.UI;

public abstract partial class Control
{
    #region Styles
    public StyleSet StyleAliasses { get; }

    public StyleSet StyleClasses { get; }

    public StyleSet PseudoClasses { get; }

    /// <summary>
    /// A unique style identifier, similar to #id in CSS.
    /// </summary>
    public string? StyleIdentifier { get; set; }

    /// <summary>
    /// ID of a style prototype to use instead of the default theme. Resolved via UIManager.
    /// </summary>
    public string? StylesheetOverride { get; set; }

    // Cached style property values for the current stylesheet/classes
    // <property name (bool, value)>.
    private Dictionary<string, (bool Found, object? Value)>? _styleCache;

    internal void InvalidateStyleCache() => _styleCache = null;

    internal void AnnounceThemeUpdate()
    {
        InvalidateStyleCache();
        OnThemeUpdated();
        foreach (var child in Children)
            child.AnnounceThemeUpdate();
    }

    /// <summary>
    /// Called when this control theme in the control tree changed or the main UiManager theme has updated.
    /// </summary>
    protected virtual void OnThemeUpdated()
    {

    }

    /// <summary>
    /// Resolves a style property for this control and returns the value,
    /// returns <paramref name="fallback"/> if no property exist.
    /// </summary>
    public T GetStyleProperty<T>(string name, T fallback)
        => TryGetStyleProperty<T>(name, out var value) && value is not null ? value : fallback;

    /// <summary>
    /// Resolves a style property for this control and
    /// returns the value from whichever matching rule has the highest <see cref="StyleClass.Specificity"/>.
    /// </summary>
    public bool TryGetStyleProperty<T>(string name, out T? value)
    {
        _styleCache ??= new();

        if (_styleCache.TryGetValue(name, out var cached))
        {
            value = cached.Found ? (T?)cached.Value : default;
            return cached.Found;
        }

        var sheet = FindEffectiveStylesheet();

        StyleRule? best = null;

        foreach (var rule in sheet.Rules)
        {
            if (!rule.Properties.ContainsKey(name))
                continue;

            if (!rule.StyleClass.Matches(this))
                continue;

            if (best is null || rule.StyleClass.Specificity >= best.StyleClass.Specificity)
                best = rule;
        }

        if (best is null)
        {
            _styleCache[name] = (false, null);
            value = default;
            return false;
        }

        try
        {
            var converted = DataFieldConverter.Convert(typeof(T), best.Properties[name]);
            _styleCache[name] = (true, converted);
            value = (T?)converted;
            return true;
        }
        catch
        {
            _styleCache[name] = (false, null);
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Walks up from this control to the nearest ancestor with a
    /// <see cref="StylesheetOverride"/> set. Falls back to the UIManager's ActiveTheme if
    /// nothing in the chain has one - never null.
    /// </summary>
    private StylePrototype FindEffectiveStylesheet()
    {
        var ui = IoCManager.Resolve<UIManager>();

        for (var control = this; control is not null; control = control.Parent)
        {
            if (control.StylesheetOverride is not null)
            {
                var resolved = ui.ResolveStyleId(control.StylesheetOverride);
                if (resolved is not null)
                    return resolved;
            }
        }

        return ui.ActiveTheme;
    }

    #endregion
}
