using System.Linq;
using Engine.Shared.Console;
using Engine.Shared.GameObjects;
using Engine.Shared.IoC;

namespace Engine.Shared.Debug.ViewVariables;

public sealed class ViewVariablesCommand : IConsoleCommand
{
    public string Name => "vv";
    public string Description => "Opens a View Variables window on an entity.";
    public string Help => "vv <uid>";

    public void Execute(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1 || !EntityUid.TryParse(args[0], out var uid))
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        var entMan = IoCManager.Resolve<EntityManager>();
        if (entMan.GetEntity(uid) is null)
        {
            shell.WriteError($"No entity with uid {uid.Id}.");
            return;
        }

        var vv = IoCManager.Resolve<ViewVariablesManager>();
        if (!vv.TryRequestOpen(VVPath.Of(VVRoot.Entity(uid))))
            shell.WriteError("vv requires a client with UI.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 0)
            return CompletionResult.Empty;

        var entMan = IoCManager.Resolve<EntityManager>();
        return CompletionResult.FromOptions(entMan.GetEntities().Select(u => u.Id.ToString()).Take(200), Help);
    }
}
