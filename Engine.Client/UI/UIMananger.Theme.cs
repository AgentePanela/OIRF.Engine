using Engine.Client.Inputs;

namespace Engine.Client.UI;

public sealed partial class UIManager
{
    private ProtoId<StylePrototype> _defaultStyleId = "EngineDefault";
    private StylePrototype _defaultStyleProto;
    private StylePrototype? _activeTheme;

    /// <summary>
    /// The theme every control falls back to when nothing in its own ancestor chain has a
    /// <see cref="Control.StylesheetOverride"/>.
    /// </summary>
    public StylePrototype ActiveTheme
    {
        get => _activeTheme ?? _defaultStyleProto;
        private set => _activeTheme = value;
    }

    public void SetMainTheme(ProtoId<StylePrototype> id)
    {
        if (!_protoMan.TryIndex(id, out var theme))
            return;

        ActiveTheme = theme;
        Root.AnnounceThemeUpdate();
    }

    /// <summary>
    /// Resolves a style prototype ID to its StylePrototype, or null if not found.
    /// </summary>
    public StylePrototype? ResolveStyleId(string? id)
    {
        if (id is null)
            return null;

        return _protoMan.TryIndex<StylePrototype>(id, out var proto) ? proto : null;
    }
}