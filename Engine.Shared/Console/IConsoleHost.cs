using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Engine.Shared.Networking;

namespace Engine.Shared.Console;

/// <summary>
/// Owns the registry of known console commands and hands out IConsoleShell -
/// One instance per process (client or server), resolved via IoC.
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
    /// Gets (creating if needed) the shell used to talk to a specific connected client (SERVER-SIDE).
    /// </summary>
    IConsoleShell GetSessionShell(INetSession session);

    /// <summary>
    /// Raised (CLIENT-SIDE) when the local shell output should be cleared. Output itself doesn't
    /// get a dedicated event - <see cref="IConsoleShell.WriteLine"/>/<c>WriteError</c> just go
    /// through <see cref="Log"/> like anything else, and <see cref="OnEngineLog"/>/<see cref="LogBacklog"/>
    /// below are the one channel a console window needs to mirror it.
    /// </summary>
    event Action? OnLocalClear;

    /// <summary>
    /// Recent engine log lines (prefix, message, terminal color), oldest first, capped to a fixed
    /// size. Lets a console window opened mid-session show what already happened before it existed
    /// - most Log calls (cvars, prototypes, components...) fire during boot.
    /// </summary>
    IReadOnlyList<(string Prefix, string Text, ConsoleColor Color)> LogBacklog { get; }

    /// <summary>
    /// Raised (CLIENT-SIDE) for every Log line written from here on, mirroring the terminal - use
    /// alongside <see cref="LogBacklog"/> to also show new lines as they happen.
    /// </summary>
    event Action<string, string, ConsoleColor>? OnEngineLog;
}
