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

    private string? _styleIdentifier;

    /// <summary>
    /// A unique style identifier, similar to #id in CSS.
    /// </summary>
    public string? StyleIdentifier
    {
        get => _styleIdentifier;
        set
        {
            if (_styleIdentifier == value)
                return;

            _styleIdentifier = value;
            InvalidateStyleCache();

            UIManager.InvalidateLayout();
        }
    }

    private string? _stylesheetOverride;

    /// <summary>
    /// ID of a style prototype to use instead of the default theme. Resolved via UIManager.
    /// </summary>
    public string? StylesheetOverride
    {
        get => _stylesheetOverride;
        set
        {
            if (_stylesheetOverride == value)
                return;

            _stylesheetOverride = value;
            AnnounceThemeUpdate();
        }
    }

    // Cached style property values for the current stylesheet/classes
    // <property name (bool, value)>.
    private Dictionary<string, (bool Found, object? Value)>? _styleCache;

    internal void InvalidateStyleCache()
    {
        _styleCache = null;
    }

    internal void AnnounceThemeUpdate()
    {
        InvalidateStyleCache();
        UIManager.InvalidateLayout();
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

        StyleRule? best = null;

        foreach (var sheet in EffectiveStylesheets())
        {
            foreach (var rule in sheet.Rules)
            {
                if (!rule.Properties.ContainsKey(name))
                    continue;

                if (!rule.StyleClass.Matches(this))
                    continue;

                if (best is null || rule.StyleClass.Specificity >= best.StyleClass.Specificity)
                    best = rule;
            }

            if (best is not null)
                break;
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
    /// Every <see cref="StylesheetOverride"/> found walking up from this control, nearest
    /// ancestor first, followed by the UIManager's ActiveTheme as the final fallback.
    /// </summary>
    private IEnumerable<StylePrototype> EffectiveStylesheets()
    {
        var ui = IoCManager.Resolve<UIManager>();
        HashSet<string>? seen = null;

        for (var control = this; control is not null; control = control.Parent)
        {
            if (control.StylesheetOverride is null)
                continue;

            var resolved = ui.ResolveStyleId(control.StylesheetOverride);
            if (resolved is null)
                continue;

            // two ancestors could point at the same override id - only yield it once
            seen ??= new();
            if (seen.Add(resolved.ID))
                yield return resolved;
        }

        yield return ui.ActiveTheme;
    }

    #endregion
}
