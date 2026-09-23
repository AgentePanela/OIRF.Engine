using Engine.Client.GameStates;
using Engine.Client.UI;
using Engine.Client.UI.Debug;
using Engine.Shared.Console;
using Engine.Shared.IoC;

namespace Engine.Client.Console;

public sealed class NetDebugCommand : IConsoleCommand
{
    private const string OverlayName = "netdebug";

    public string Name => "netdebug";
    public string Description => "Toggles the networking debug overlay: netdebug [entity rows]";
    public string Help => "netdebug [entity rows]";

    [Dependency] private readonly UIManager _ui = default!;

    public NetDebugCommand() => IoCManager.ResolveDependencies(this);

    public void Execute(IConsoleShell shell, string[] args)
    {
        var rows = NetDebugOverlay.DefaultTopEntities;
        if (args.Length > 1 || (args.Length == 1 && !int.TryParse(args[0], out rows)) || rows <= 0)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        if (_ui.GetOverlay<NetDebugOverlay>(OverlayName) is { } existing)
        {
            _ui.RemoveOverlay(existing);
            shell.WriteLine("Net debug overlay off.");
            return;
        }

        _ui.AddOverlay(new NetDebugOverlay(rows), OverlayName);
        shell.WriteLine($"Net debug overlay on ({rows} entity rows).");
    }
}

public sealed class NetIdsCommand : IConsoleCommand
{
    public string Name => "netids";
    public string Description => "Toggles the net id labels drawn over replicated entities";
    public string Help => "netids";

    public void Execute(IConsoleShell shell, string[] args)
    {
        if (!IoCManager.TryResolve<NetIdsDrawSystem>(out var system) || system is null)
        {
            shell.WriteError("The entity systems are not up yet.");
            return;
        }

        system.Enabled = !system.Enabled;
        shell.WriteLine($"Net id labels {(system.Enabled ? "on" : "off")}.");
    }
}
