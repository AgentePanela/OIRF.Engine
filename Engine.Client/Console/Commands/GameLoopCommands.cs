using Engine.Shared.Console;

namespace Engine.Client.Console.Commands;

public sealed class ExitCommand : IConsoleCommand
{
    public string Name => "exit";
    public string Description => "Closes the application.";

    public string Help => Name;

    public void Execute(IConsoleShell shell, string[] args)
    {
        GameClient.Instance.Exit();
    }
}