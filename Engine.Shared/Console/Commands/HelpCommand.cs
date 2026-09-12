using System.Linq;

namespace Engine.Shared.Console.Commands;

/// <summary>
/// Lists every registered command, or describes one by name.
/// </summary>
public sealed class HelpCommand : IConsoleCommand
{
    public string Name => "help";
    public string Description => "Lists every command, or describes one: help <command>";

    public void Execute(IConsoleShell shell, string[] args)
    {
        var commands = shell.ConsoleHost.AvailableCommands;

        if (args.Length == 0)
        {
            foreach (var cmd in commands.Values.OrderBy(c => c.Name))
                shell.WriteLine($"{cmd.Name} - {cmd.Description}");
            return;
        }

        if (!commands.TryGetValue(args[0], out var found))
        {
            shell.WriteError($"Unknown command: '{args[0]}'");
            return;
        }

        shell.WriteLine($"{found.Name} - {found.Description}");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length > 0)
            return CompletionResult.Empty;

        return CompletionResult.FromOptions(
            shell.ConsoleHost.AvailableCommands.Values.Select(c => c.Name),
            hint: "<command>");
    }
}
