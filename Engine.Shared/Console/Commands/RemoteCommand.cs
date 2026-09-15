using System.Linq;

namespace Engine.Shared.Console.Commands;

/// <summary>
/// Forces the rest of the command line to run on the server.
/// </summary>
public sealed class RemoteCommand : IConsoleCommand
{
    public string Name => ">";
    public string Description => "Runs the rest of the line on the server: > <command> [args...]";
    public string Help => "> <command> [args...]";

    public void Execute(IConsoleShell shell, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteError("Usage: > <command> [args...]");
            return;
        }

        shell.RemoteExecuteCommand(Requote(args));
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        => shell.ConsoleHost.GetOrRequestRemoteCompletions(Requote(args) + " ");

    // args are already split, requote anything with whitespace
    private static string Requote(string[] args)
        => string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
}
