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

/// <summary>
/// A tag with no content and no closing tag, like [icon=...]. Puts something at its place instead of styling the
/// text after it.
/// </summary>
public interface IInlineMarkupTag : IMarkupTag
{
    FormattedStyle IMarkupTag.Apply(FormattedStyle current, string? value) => current;

    /// <summary>
    /// What goes where the tag is, or null to drop it.
    /// </summary>
    FormattedSegment? Insert(FormattedStyle current, string? value);
}
