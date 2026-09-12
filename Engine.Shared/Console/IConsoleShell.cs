using Engine.Shared.Networking;

namespace Engine.Shared.Console;

/// <summary>
/// The console shell instance. -
/// The server keeps one instance per connected session  (plus its own local shell); the client only ever 
/// has its local shell.
/// </summary>
public interface IConsoleShell
{
    IConsoleHost ConsoleHost { get; }

    /// <summary>
    /// Is the shell running in a local context.
    /// </summary>
    bool IsLocal => Session is null;

    /// <summary>
    /// Is the shell running on the server?
    /// </summary>
    bool IsServer { get; }

    /// <summary>
    /// Is the shell running on the client?
    /// </summary>
    bool IsClient => !IsServer;

    /// <summary>
    /// The session peer that owns the console if its multiplayer.
    /// </summary>
    INetSession? Session { get; }

    void ExecuteCommand(string command);

    /// <summary>
    /// Executes the command string on the remote peer. This is mainly used to forward commands from the client to the server.
    /// If there is no remote peer (this is a local shell), this function does nothing.
    /// </summary>
    void RemoteExecuteCommand(string command);

    void WriteLine(string text);

    void WriteError(string text);

    /// <summary>
    /// Clears the entire console of text.
    /// </summary>
    void Clear();
}