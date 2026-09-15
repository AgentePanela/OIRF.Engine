using System.Collections.Generic;
using Engine.Shared.Networking;

namespace Engine.Shared.Console;

/// <summary>
/// A console command.
/// </summary>
public interface IConsoleCommand
{
    string Name { get; }
    string Description { get; }
    string Help { get; }

    bool RequireServerOrSingleplayer => false;

    void Execute(IConsoleShell shell, string[] args);

    /// <summary>
    /// Completion options for the argument currently being typed. <paramref name="args"/> is
    /// every argument already fully typed (not including the partial one)
    /// </summary>
    CompletionResult GetCompletion(IConsoleShell shell, string[] args) => CompletionResult.Empty;
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

/// <summary>
/// Sent client to server, to know the server completion list.
/// </summary>
public sealed partial class MsgCompletionRequest : NetMessage
{
    public string Line { get; private set; } = "";

    public MsgCompletionRequest(string line)
    {
        Line = line;
    }
}

/// <summary>
/// Sent server to client with the answer to a <see cref="MsgCompletionRequest"/>.
/// </summary>
public sealed partial class MsgCompletionResponse : NetMessage
{
    public string Line { get; private set; } = "";
    public List<string> Values { get; private set; } = new();
    public List<string> Hints { get; private set; } = new();
    public string Hint { get; private set; } = "";

    public MsgCompletionResponse(string line, CompletionResult result)
    {
        Line = line;
        Hint = result.Hint ?? "";

        foreach (var option in result.Options)
        {
            Values.Add(option.Value);
            Hints.Add(option.Hint ?? "");
        }
    }

    public CompletionResult ToResult()
    {
        var options = new List<CompletionOption>(Values.Count);
        for (var i = 0; i < Values.Count; i++)
            options.Add(new CompletionOption(Values[i], Hints[i].Length == 0 ? null : Hints[i]));

        return new CompletionResult(options, Hint.Length == 0 ? null : Hint);
    }
}
