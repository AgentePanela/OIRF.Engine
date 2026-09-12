using Engine.Shared.IoC;
using Engine.Shared.Networking;

namespace Engine.Shared.Console.Commands;

public sealed class PingCommand : IConsoleCommand
{
    public string Name => "ping";
    public string Description => "Replies with Pong!, plus the current connection's latency if there is one.";

    [Dependency] private readonly INetManager _netMan = default!;

    public PingCommand() => IoCManager.ResolveDependencies(this);

    public void Execute(IConsoleShell shell, string[] args)
    {
        if (!_netMan.IsRunning)
        {
            shell.WriteLine("Not connected to a server!");
            return;
        }

        shell.WriteLine("Pong!");
        if (_netMan.MySession is { } session)
            shell.WriteLine($"Latency: {session.Ping}ms");
    }
}
