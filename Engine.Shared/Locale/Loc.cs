using Engine.Shared.IoC;
using Engine.Shared.Locale;

// no namespace

/// <summary>
/// Static convenience wrapper for the <see cref="ILocalizationManager"/>.
/// </summary>
public static class Loc
{
    private static ILocalizationManager _loc => IoCManager.Resolve<ILocalizationManager>();

    /// <inheritdoc cref="ILocalizationManager.GetString(string)"/>>
    public static string GetString(string key)
        => _loc.GetString(key);

    /// <inheritdoc cref="ILocalizationManager.GetString(string, ValueTuple{string, object}[])"/>>
    public static string GetString(string key, params (string, object)[] args)
        => _loc.GetString(key, args);
}
