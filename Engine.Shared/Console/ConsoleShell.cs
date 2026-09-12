using System;
using System.Collections.Generic;
using System.Text;
using Engine.Shared.IoC;
using Engine.Shared.Networking;

namespace Engine.Shared.Console;

internal sealed class ConsoleShell : IConsoleShell
{
    [Dependency] private INetManager _netMan = default!;
    [Dependency] private SharedContentManager _sharedContent = default!;

    private readonly ConsoleHost _host;

    public IConsoleHost ConsoleHost => _host;
    public INetSession? Session { get; }
    public bool IsServer => _sharedContent.IsServer();

    public ConsoleShell(ConsoleHost host, INetSession? session)
    {
        _host = host;
        Session = session;
        IoCManager.ResolveDependencies(this);
    }

    public void ExecuteCommand(string command)
    {
        var line = command.Trim();
        if (line.Length == 0)
            return;

        var parts = Tokenize(line);
        if (parts.Length == 0)
            return;

        var name = parts[0];
        var args = parts[1..];

        var isLocal = Session is null;

        if (!_host.TryGetCommand(name, out var cmd))
        {
            // don know this command locally
            if (isLocal && !IsServer)
            {
                RemoteExecuteCommand(line);
                return;
            }

            WriteError($"Unknown command: '{name}'");
            return;
        }

        if (cmd.RequireServerOrSingleplayer && !IsServer)
        {
            RemoteExecuteCommand(line);
            return;
        }

        try
        {
            cmd.Execute(this, args);
        }
        catch (Exception e)
        {
            WriteError($"'{name}' threw an exception: {e.Message}");
            Log.Error(e);
        }
    }

    public void RemoteExecuteCommand(string command)
    {
        if (Session is not null)
            return; // only the owning client forwards its own input

        if (!_netMan.IsClient || _netMan.MySession is null)
        {
            WriteError("Not connected to a server.");
            return;
        }

        _netMan.MySession.SendMessage(new MsgExecuteCommand(command));
    }

    public void WriteLine(string text) => Reply(text, isError: false);

    public void WriteError(string text) => Reply(text, isError: true);

    public void Clear()
    {
        if (Session is not null)
            Session.SendMessage(new MsgConsoleReply("", false, clear: true));
        else if (!IsServer)
            _host.RaiseLocalClear();
    }

    private void Reply(string text, bool isError)
    {
        if (Session is not null)
        {
            Session.SendMessage(new MsgConsoleReply(text, isError, clear: false));
            return;
        }

        if (isError)
            Log.Error(text);
        else
            Log.Debug(text);
    }

    public static string[] Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '\\' && i + 1 < line.Length && (line[i + 1] == '"' || line[i + 1] == '\\'))
                {
                    current.Append(line[i + 1]);
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = false;
                    continue;
                }

                current.Append(c);
                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                hasToken = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                continue;
            }

            current.Append(c);
            hasToken = true;
        }

        if (hasToken)
            tokens.Add(current.ToString());

        return tokens.ToArray();
    }
}
