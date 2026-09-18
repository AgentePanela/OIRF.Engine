using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Engine.Shared.Networking;

namespace Engine.Shared.Console;

/// <summary>
/// Owns the registry of known console commands and hands out IConsoleShell -
/// One instance per process (client or server)
/// </summary>
public interface IConsoleHost
{
    internal void Init();

    /// <summary>
    /// All commands currently registered, keyed by <see cref="IConsoleCommand.Name"/>.
    /// </summary>
    IReadOnlyDictionary<string, IConsoleCommand> AvailableCommands { get; }

    /// <summary>
    /// The shell for this own side.
    /// </summary>
    IConsoleShell LocalShell { get; }

    /// <summary>
    /// Registers a command instance. Commands are normally discovered automatically on loading.
    /// </summary>
    void RegisterCommand(IConsoleCommand command);

    bool TryGetCommand(string name, [NotNullWhen(true)] out IConsoleCommand? command);

    /// <summary>
    /// Disables/re-enables a registered command without unregistering it - a disabled command is
    /// rejected by <see cref="IConsoleShell.ExecuteCommand"/> and hidden from
    /// <see cref="GetCompletions"/>'s command-name suggestions.
    /// </summary>
    void SetCommandEnabled(string name, bool enabled);

    /// <summary>
    /// Whether the named command is registered and not currently disabled.
    /// </summary>
    bool IsCommandEnabled(string name);

    /// <summary>
    /// Autocomplete for a command line as currently typed
    /// </summary>
    CompletionResult GetCompletions(string line);

    /// <summary>
    /// CLIENT-SIDE, for a command (like ">") whose own completions depend on the server
    /// </summary>
    CompletionResult GetOrRequestRemoteCompletions(string line);

    /// <summary>
    /// Raised (CLIENT-SIDE) with a server's reply to <see cref="GetOrRequestRemoteCompletions"/>
    /// </summary>
    event Action<string, CompletionResult>? OnRemoteCompletions;

    /// <summary>
    /// Gets (creating if needed) the shell used to talk to a specific connected client (SERVER-SIDE).
    /// </summary>
    IConsoleShell GetSessionShell(INetSession session);

    /// <summary>
    /// Raised (CLIENT-SIDE) when the local shell output should be cleared.
    /// </summary>
    event Action? OnLocalClear;

    /// <summary>
    /// Recent engine log lines. Prefix is null for a prefix-less (<see cref="Log.Blank"/>) line.
    /// LevelColor is the body text's color (e.g. red for an error), null to use the console's
    /// own default text color.
    /// </summary>
    IReadOnlyList<(string? Prefix, string Text, ConsoleColor Color, ConsoleColor? LevelColor)> LogBacklog { get; }

    /// <summary>
    /// Raised (CLIENT-SIDE) for every Log line written from here on, mirroring the terminal.
    /// Prefix is null for a prefix-less (<see cref="Log.Blank"/>) line. LevelColor is the body
    /// text's color (e.g. red for an error), null to use the console's own default text color.
    /// </summary>
    event Action<string?, string, ConsoleColor, ConsoleColor?>? OnEngineLog;
}
