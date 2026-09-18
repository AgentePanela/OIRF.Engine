using Engine.Client.Scenes;
using Engine.Client.Scenes.Factories;
using Engine.Shared.Console;
using Engine.Shared.IoC;

namespace Engine.Client.Console.Commands;

public sealed class ChangeSceneCommand : IConsoleCommand
{
    public string Name => "change-scene";

    public string Description => "Change the current scene.";

    public string Help => Name + " <scene id>";
    
    [Dependency] private readonly SceneFactory _sceneFac = default!;
    [Dependency] private readonly SceneManager _sceneMan = default!;

    public ChangeSceneCommand() => IoCManager.ResolveDependencies(this);

    public void Execute(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteLine("You must select a scene!");
            return;
        }

        var sceneType = _sceneFac.CreateInstance(args[0]);
        if (sceneType is null)
        {
            shell.WriteError("Unknown scene.");
            return;
        }

        _sceneMan.ChangeScene(sceneType);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return CompletionResult.FromOptions(_sceneFac.Scenes.Keys);
    }
}