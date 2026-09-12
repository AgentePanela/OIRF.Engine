using Engine.Shared.Networking;

namespace Engine.Shared.Console;

/// <summary>
/// A console command.
/// </summary>
public interface IConsoleCommand
{
    string Name { get; }
    string Description { get; }

    bool RequireServerOrSingleplayer => false;
    
    void Execute(IConsoleShell shell, string[] args);
}

/// <summary>
/// Sent server > client with the output of a command.
/// </summary>
public sealed partial class MsgConsoleReply : NetMessage
{
    public string Text { get; private set; } = "";
    public bool IsError { get; private set; }
    public bool Clear { get; private set; }

    public MsgConsoleReply(string text, bool isError, bool clear = false)
    {
        Text = text;
        IsError = isError;
        Clear = clear;
    }
}

/// <summary>
/// Sent from client to server to run a console command line the client doenst know locally (or that is
/// marked <see cref="IConsoleCommand.RequireServerOrSingleplayer"/>)
/// </summary>
public sealed partial class MsgExecuteCommand : NetMessage
{
    public string CommandLine { get; private set; } = "";

    public MsgExecuteCommand(string commandLine)
    {
        CommandLine = commandLine;
    }
}
