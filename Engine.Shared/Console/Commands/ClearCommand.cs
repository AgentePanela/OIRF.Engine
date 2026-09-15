namespace Engine.Shared.Console.Commands;

/// <summary>
/// Clears the local console output.
/// </summary>
public sealed class ClearCommand : IConsoleCommand
{
    public string Name => "clear";
    public string Description => "Clears the console output.";

    public void Execute(IConsoleShell shell, string[] args) => shell.Clear();
}
