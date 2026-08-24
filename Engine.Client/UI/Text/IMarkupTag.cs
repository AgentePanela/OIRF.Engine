namespace Engine.Client.UI;

/// <summary>
/// A BBCode-style tag usable inside <see cref="FormattedMessage.Parse"/>.
/// </summary>
public interface IMarkupTag
{
    /// <summary>
    /// Tag name matched case-insensive against [name]
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Computes the style for everything inside this tag.
    /// </summary>
    FormattedStyle Apply(FormattedStyle current, string? value);
}
