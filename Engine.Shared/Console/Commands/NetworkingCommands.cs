using Engine.Shared.IoC;
using Engine.Shared.Networking;

namespace Engine.Shared.Console.Commands;

public sealed class ConnectCommand : IConsoleCommand
{
    private const int DefaultPort = 1313;

    public string Name => "connect";
    public string Description => "Connects to a server: connect <host> [port]";
    public string Help => "connect <host> [port]";

    [Dependency] private readonly INetManager _netMan = default!;

    public ConnectCommand() => IoCManager.ResolveDependencies(this);

    public void Execute(IConsoleShell shell, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError("Usage: connect <host> [port]");
            return;
        }

        var port = DefaultPort;
        if (args.Length == 2 && !int.TryParse(args[1], out port))
        {
            shell.WriteError($"Invalid port: '{args[1]}'");
            return;
        }

        shell.WriteLine($"Connecting to {args[0]}:{port}...");
        _netMan.ConnectClient(args[0], port);
    }
}

public sealed class DisconnectCommand : IConsoleCommand
{
    public string Name => "disconnect";
    public string Description => "Disconnects from the current server: disconnect [reason]";
    public string Help => "disconnect [reason]";

    [Dependency] private readonly INetManager _netMan = default!;

    public DisconnectCommand() => IoCManager.ResolveDependencies(this);

    public void Execute(IConsoleShell shell, string[] args)
    {
        if (!_netMan.IsClient)
        {
            shell.WriteError("Not connected to a server.");
            return;
        }

        _netMan.DisconnectClient(args.Length > 0 ? string.Join(' ', args) : "Disconnected via console.");
    }
}

public sealed class PingCommand : IConsoleCommand
{
    public string Name => "ping";
    public string Description => "Replies with Pong! (and the latency)";
    public string Help => "ping";

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
