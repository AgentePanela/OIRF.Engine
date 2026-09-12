using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Engine.Client.Inputs;
using Engine.Shared.Console;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Engine.Client.UI.Debug;

/// <summary>
/// Debug console window (like RT's) - types a command line, runs it through the local
/// <see cref="IConsoleShell"/>, and prints whatever comes back (locally, or forwarded from the server).
/// </summary>
public sealed class ConsoleWindow : Window
{
    private const int MaxLines = 500;

    private static readonly Color NormalColor = Color.White;
    private static readonly Color ErrorColor = new(0xFF, 0x55, 0x55);
    private static readonly Color EchoColor = new(0x55, 0xFF, 0xFF);

    [Dependency] private readonly IConsoleHost _consoleHost = default!;
    [Dependency] private readonly UIManager _ui = default!;
    [Dependency] private readonly InputManager _inputManager = default!;

    private readonly BoxContainer _lines;
    private readonly ScrollContainer _scroll;
    private readonly LineEdit _inputLine;

    private readonly List<string> _history = new();
    private int _historyIndex;

    private readonly ConcurrentQueue<(string Prefix, string Text, ConsoleColor Color)> _pendingLogs = new();
    private bool _pendingScrollToBottom;

    public ConsoleWindow()
    {
        IoCManager.ResolveDependencies(this);

        //StylesheetOverride = "EngineDefault";
        Title = "Console";
        MinWidth = 720;
        MinHeight = 420;
        StyleClasses.Add("console");

        var root = new BoxContainer { Orientation = Orientation.Vertical, Separation = 4, VerticalExpand = true };
        AddChild(root);

        _lines = new BoxContainer { Orientation = Orientation.Vertical, Separation = 0 };

        _scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = false };
        _scroll.AddChild(_lines);
        root.AddChild(_scroll);

        _inputLine = new LineEdit { PlaceholderText = "Type a command...", HorizontalExpand = true };
        _inputLine.OnTextEntered += OnSubmit;
        root.AddChild(_inputLine);

        _consoleHost.OnLocalClear += OnLocalClear;
        foreach (var entry in _consoleHost.LogBacklog)
            AddLine($"[{entry.Prefix}] {entry.Text}", MapConsoleColor(entry.Color));

        _consoleHost.OnEngineLog += OnLog;

        ResetHistoryCursor();
        FocusInput();
    }

    /// <summary>
    /// Puts keyboard focus on the input line
    /// </summary>
    public void FocusInput() => _ui.SetFocus(_inputLine);

    protected override void Update(float dt)
    {
        base.Update(dt);

        // Apply last frame requested scroll now 
        if (_pendingScrollToBottom)
        {
            _scroll.ScrollToBottom();
            _pendingScrollToBottom = false;
        }

        if (!_inputLine.IsFocused)
            return;

        if (_inputManager.KeyPressed(Keys.Up))
            NavigateHistory(-1);
        else if (_inputManager.KeyPressed(Keys.Down))
            NavigateHistory(1);

        while (_pendingLogs.TryDequeue(out var entry))
            AddLine($"[{entry.Prefix}] {entry.Text}", MapConsoleColor(entry.Color));
    }

    protected override void OnDispose()
    {
        _consoleHost.OnLocalClear -= OnLocalClear;
        _consoleHost.OnEngineLog -= OnLog;
        base.OnDispose();
    }

    private void OnSubmit(string text)
    {
        var line = text.Trim();
        _inputLine.Text = "";

        if (line.Length == 0)
            return;

        _history.Add(line);
        ResetHistoryCursor();

        AddLine($"> {line}", EchoColor);
        _consoleHost.LocalShell.ExecuteCommand(line);
    }

    private void NavigateHistory(int direction)
    {
        if (_history.Count == 0)
            return;

        _historyIndex = Math.Clamp(_historyIndex + direction, 0, _history.Count);
        _inputLine.Text = _historyIndex < _history.Count ? _history[_historyIndex] : "";
    }

    private void ResetHistoryCursor() => _historyIndex = _history.Count;

    private void OnLocalClear() => _lines.ClearChildren();

    private void OnLog(string prefix, string text, ConsoleColor color) => _pendingLogs.Enqueue((prefix, text, color));

    private static Color MapConsoleColor(ConsoleColor color) => color switch
    {
        ConsoleColor.Yellow => new Color(0xFF, 0xE1, 0x5A),
        ConsoleColor.Red => ErrorColor,
        ConsoleColor.DarkMagenta => new Color(0x9A, 0x4D, 0xC7),
        ConsoleColor.Cyan => EchoColor,
        _ => NormalColor,
    };

    private void AddLine(string text, Color color)
    {
        // Only stick to the bottom if the user was already there
        var stickToBottom = IsScrolledToBottom();

        _lines.AddChild(new Label { Text = text, Color = color });

        while (_lines.Children.Count > MaxLines)
            _lines.RemoveChild(_lines.Children[0], dispose: true);

        if (stickToBottom)
            _pendingScrollToBottom = true;
    }

    private bool IsScrolledToBottom()
    {
        const float Slack = 4f;
        return _scroll.MaxScrollOffset.Y - _scroll.ScrollOffset.Y <= Slack;
    }
}
