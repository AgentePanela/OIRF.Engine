using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Engine.Shared.IoC;
using Engine.Shared.Networking;

namespace Engine.Shared.Console;

internal sealed class ConsoleHost : IConsoleHost
{
    [Dependency] private INetManager _netMan = default!;
    [Dependency] private SharedContentManager _sharedContent = default!;

    private readonly Dictionary<string, IConsoleCommand> _commands = new();
    private readonly HashSet<string> _disabledCommands = new();
    private readonly Dictionary<string, IConsoleShell> _sessionShells = new();

    private const int MaxLogBacklog = 500;
    private readonly System.Threading.Lock _logLock = new();
    private readonly List<(string? Prefix, string Text, ConsoleColor Color, ConsoleColor? LevelColor)> _logBacklog = new();

    public IConsoleShell LocalShell { get; private set; } = default!;
    public IReadOnlyDictionary<string, IConsoleCommand> AvailableCommands => _commands;

    public IReadOnlyList<(string? Prefix, string Text, ConsoleColor Color, ConsoleColor? LevelColor)> LogBacklog
    {
        get { lock (_logLock) return _logBacklog.ToArray(); }
    }

    public event Action? OnLocalClear;
    public event Action<string?, string, ConsoleColor, ConsoleColor?>? OnEngineLog;
    public event Action<string, CompletionResult>? OnRemoteCompletions;

    // Only one remote completion request is ever relevant at a time
    private string? _lastRemoteLine;
    private CompletionResult _lastRemoteResult = CompletionResult.Empty;

    void IConsoleHost.Init()
    {
        IoCManager.ResolveDependencies(this);
        LocalShell = new ConsoleShell(this, null);
        
        DiscoverCommands();
        if (!_sharedContent.IsServer())
            Log.OnLog += OnEngineLogWritten;

        _netMan.RegisterNetMessage<MsgExecuteCommand>(OnExecuteCommandReceived);
        _netMan.RegisterNetMessage<MsgConsoleReply>((msg, _) => OnConsoleReplyReceived(msg));
        _netMan.RegisterNetMessage<MsgCompletionRequest>(OnCompletionRequestReceived);
        _netMan.RegisterNetMessage<MsgCompletionResponse>((msg, _) => OnCompletionResponseReceived(msg));
        _netMan.OnDisconnected += (_, args) =>
        {
            if (args.Session is not null)
                _sessionShells.Remove(args.Session.SessionId);
        };
    }

    private void DiscoverCommands()
    {
        foreach (var assembly in _sharedContent.GetAssemblies())
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || !typeof(IConsoleCommand).IsAssignableFrom(type))
                    continue;

                RegisterCommand((IConsoleCommand)Activator.CreateInstance(type)!);
            }
        }
    }

    public void RegisterCommand(IConsoleCommand command)
    {
        if (!_commands.TryAdd(command.Name, command))
            throw new InvalidOperationException($"Console command '{command.Name}' is already registered!");
    }

    public bool TryGetCommand(string name, [NotNullWhen(true)] out IConsoleCommand? command)
        => _commands.TryGetValue(name, out command);

    public string GetCommandName<T>() where T : IConsoleCommand
    {
        foreach (var command in _commands.Values)
        {
            if (command is T)
                return command.Name;
        }

        throw new InvalidOperationException($"No registered command of type '{typeof(T).Name}'.");
    }

    public void SetCommandEnabled(string name, bool enabled)
    {
        if (enabled)
            _disabledCommands.Remove(name);
        else
            _disabledCommands.Add(name);
    }

    public bool IsCommandEnabled(string name)
        => _commands.ContainsKey(name) && !_disabledCommands.Contains(name);

    public CompletionResult GetCompletions(string line)
    {
        var endsWithSpace = line.Length == 0 || char.IsWhiteSpace(line[^1]);
        var args = ConsoleShell.Tokenize(line);

        CompletionResult raw;
        string partial;

        if (args.Length == 0 || (args.Length == 1 && !endsWithSpace))
        {
            // still typing the command name itself
            partial = args.Length == 0 ? "" : args[0];
            raw = new CompletionResult(_commands.Values
                .Where(c => !_disabledCommands.Contains(c.Name))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new CompletionOption(c.Name, c.Description))
                .ToList());
        }
        else
        {
            if (!TryGetCommand(args[0], out var cmd))
                return CompletionResult.Empty;

            var commandArgs = args[1..];
            if (!endsWithSpace && commandArgs.Length > 0)
                commandArgs = commandArgs[..^1]; // drop the inprogress partial token

            partial = endsWithSpace ? "" : args[^1];
            raw = cmd.GetCompletion(LocalShell, commandArgs);

            if (raw.Options.Count == 0 && raw.Hint is null)
                raw = new CompletionResult(raw.Options, cmd.Help); // show help if theres nothing to show
        }

        var filtered = raw.Options
            .Where(o => o.Value.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new CompletionResult(filtered, raw.Hint);
    }

    public CompletionResult GetOrRequestRemoteCompletions(string line)
    {
        if (!_netMan.IsRunning)
            return CompletionResult.Empty;
        
        if (_lastRemoteLine == line)
            return _lastRemoteResult;

        if (_netMan.MySession is { } session)
            session.SendMessage(new MsgCompletionRequest(line));

        return CompletionResult.Empty;
    }

    public IConsoleShell GetSessionShell(INetSession session)
    {
        if (!_sessionShells.TryGetValue(session.SessionId, out var shell))
            _sessionShells[session.SessionId] = shell = new ConsoleShell(this, session);

        return shell;
    }

    internal void RaiseLocalClear() => OnLocalClear?.Invoke();

    // Log can call from any thread (networking, parallel jobs, etc.)
    private void OnEngineLogWritten(string? prefix, string text, ConsoleColor color, ConsoleColor? levelColor)
    {
        lock (_logLock)
        {
            _logBacklog.Add((prefix, text, color, levelColor));
            if (_logBacklog.Count > MaxLogBacklog)
                _logBacklog.RemoveAt(0);
        }

        OnEngineLog?.Invoke(prefix, text, color, levelColor);
    }

    private void OnExecuteCommandReceived(MsgExecuteCommand msg, INetSession? session)
    {
        if (!_netMan.IsServer)
        {
            Log.Warn("Received a MsgExecuteCommand on a client - ignoring.");
            return;
        }

        if (session is null)
        {
            Log.Warn("Received a MsgExecuteCommand with no sender session - ignoring.");
            return;
        }

        Log.Debug($"[{session.RemoteEndPoint}] > {msg.CommandLine}");
        GetSessionShell(session).ExecuteCommand(msg.CommandLine);
    }

    private void OnCompletionRequestReceived(MsgCompletionRequest msg, INetSession? session)
    {
        if (!_netMan.IsServer || session is null)
            return;

        session.SendMessage(new MsgCompletionResponse(msg.Line, GetCompletions(msg.Line)));
    }

    private void OnCompletionResponseReceived(MsgCompletionResponse msg)
    {
        if (_netMan.IsServer)
            return;

        _lastRemoteLine = msg.Line;
        _lastRemoteResult = msg.ToResult();
        OnRemoteCompletions?.Invoke(msg.Line, _lastRemoteResult);
    }

    private void OnConsoleReplyReceived(MsgConsoleReply msg)
    {
        if (_netMan.IsServer)
        {
            Log.Warn("Received a MsgConsoleReply on the server - ignoring.");
            return;
        }

        if (msg.Clear)
        {
            RaiseLocalClear();
            return;
        }

        if (msg.IsError)
            Log.Error(msg.Text);
        else
            Log.Debug(msg.Text);
    }
}
