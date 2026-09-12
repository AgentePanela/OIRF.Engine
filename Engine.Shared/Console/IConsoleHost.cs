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
    /// Autocomplete for a command line as currently typed
    /// </summary>
    CompletionResult GetCompletions(string line);

    /// <summary>
    /// Gets (creating if needed) the shell used to talk to a specific connected client (SERVER-SIDE).
    /// </summary>
    IConsoleShell GetSessionShell(INetSession session);

    /// <summary>
    /// Raised (CLIENT-SIDE) when the local shell output should be cleared.
    /// </summary>
    event Action? OnLocalClear;

    /// <summary>
    /// Recent engine log lines
    /// </summary>
    IReadOnlyList<(string Prefix, string Text, ConsoleColor Color)> LogBacklog { get; }

    /// <summary>
    /// Raised (CLIENT-SIDE) for every Log line written from here on, mirroring the terminal
    /// </summary>
    event Action<string, string, ConsoleColor>? OnEngineLog;
}
