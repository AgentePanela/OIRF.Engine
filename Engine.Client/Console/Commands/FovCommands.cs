using Engine.Client.Graphics;
using Engine.Shared.Console;
using Engine.Shared.IoC;

namespace Engine.Client.Console.Commands;

public sealed class ToggleFovCommand : IConsoleCommand
{
    public string Name => "togglefov";
    public string Description => "Toggles the field of view of the main viewport";
    public string Help => "togglefov";

    [Dependency] private readonly IRenderManager _renderMan = default!;

    public ToggleFovCommand() => IoCManager.ResolveDependencies(this);

    public void Execute(IConsoleShell shell, string[] args)
    {
        var viewport = _renderMan.MainViewport;
        viewport.FovEnabled = !viewport.FovEnabled;
        shell.WriteLine($"Field of view {(viewport.FovEnabled ? "on" : "off")}.");
    }
}
