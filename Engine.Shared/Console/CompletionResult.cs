using System;
using System.Collections.Generic;
using System.Linq;

namespace Engine.Shared.Console;

/// <summary>
/// One completion suggestion: the text to insert and an optional short hint
/// </summary>
public readonly record struct CompletionOption(string Value, string? Hint = null);

/// <summary>
/// What a command offers as completions for the argument currently being typed.
/// </summary>
public sealed class CompletionResult
{
    public static readonly CompletionResult Empty = new(Array.Empty<CompletionOption>());

    public IReadOnlyList<CompletionOption> Options { get; }

    /// <summary>
    /// A short description of what this argument is, shown when there
    /// are no matching <see cref="Options"/>.
    /// </summary>
    public string? Hint { get; }

    public CompletionResult(IReadOnlyList<CompletionOption> options, string? hint = null)
    {
        Options = options;
        Hint = hint;
    }

    public static CompletionResult FromOptions(IEnumerable<string> values, string? hint = null)
        => new(values.Select(v => new CompletionOption(v)).ToList(), hint);
}
