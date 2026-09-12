using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Engine.Client.Inputs;
using Engine.Shared.Console;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Engine.Client.UI.Debug;

/// <summary>
/// Debug console overlay (like RT's) - a borderless dark panel across the top of the screen, not
/// a draggable Window. Types a command line, runs it through the local <see cref="IConsoleShell"/>,
/// and prints whatever comes back (locally, or forwarded from the server).
/// </summary>
public sealed class ConsoleOverlay : Overlay
{
    private const int MaxLines = 500;
    private const int MaxSuggestions = 8;

    private static readonly Color PanelBackground = new(10, 12, 20, 235);
    private static readonly Color NormalColor = Color.White;
    private static readonly Color ErrorColor = new(0xFF, 0x55, 0x55);
    private static readonly Color EchoColor = new(0x55, 0xFF, 0xFF);
    private static readonly Color HintColor = new(0x90, 0x90, 0x90);
    private static readonly Color HighlightBackground = new(0x33, 0x44, 0x66, 200);

    [Dependency] private readonly IConsoleHost _consoleHost = default!;
    [Dependency] private readonly UIManager _ui = default!;
    [Dependency] private readonly InputManager _inputManager = default!;

    private readonly BoxContainer _lines;
    private readonly ScrollContainer _scroll;
    private readonly LineEdit _inputLine;
    private readonly BoxContainer _suggestions;

    private readonly List<string> _history = new();
    private int _historyIndex;

    private readonly List<SuggestionRow> _suggestionRows = new();
    private readonly List<string> _suggestionValues = new();
    private int _suggestionIndex;

    private readonly ConcurrentQueue<(string Prefix, string Text, ConsoleColor Color)> _pendingLogs = new();
    private bool _pendingScrollToBottom;


    public ConsoleOverlay()
    {
        IoCManager.ResolveDependencies(this);

        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Top;
        Height = 420;
        Padding = new Thickness(10);
        Background = PanelBackground;
        MouseFilter = MouseFilterMode.Stop;

        var root = new BoxContainer { Orientation = Orientation.Vertical, Separation = 4, VerticalExpand = true };
        AddChild(root);

        _lines = new BoxContainer { Orientation = Orientation.Vertical, Separation = 0 };

        _scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true };
        _scroll.AddChild(_lines);
        root.AddChild(_scroll);

        _suggestions = new BoxContainer { Orientation = Orientation.Vertical, Separation = 0, Visible = false };
        root.AddChild(_suggestions);

        _inputLine = new LineEdit
        {
            PlaceholderText = "Type a command...",
            HorizontalExpand = true,
        };
        _inputLine.OnTextEntered += OnSubmit;
        _inputLine.OnTextChanged += UpdateSuggestions;
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

        // apply last frame requested scroll now
        if (_pendingScrollToBottom)
        {
            _scroll.ScrollToBottom();
            _pendingScrollToBottom = false;
        }

        if (!_inputLine.IsFocused)
            return;

        var pressed = _inputManager.KeysPressedThisFrame().ToList();
        bool WasPressed(Keys key) => pressed.Contains(key);

        if (WasPressed(Keys.Tab) && _suggestionValues.Count > 0)
        {
            ApplySuggestion(_suggestionIndex);
        }
        else if (_suggestionValues.Count > 0)
        {
            if (WasPressed(Keys.Down))
                HighlightSuggestion(_suggestionIndex + 1);
            else if (WasPressed(Keys.Up))
                HighlightSuggestion(_suggestionIndex - 1);
            else if (WasPressed(Keys.Escape))
                HideSuggestions();
        }
        else if (WasPressed(Keys.Up))
        {
            NavigateHistory(-1);
        }
        else if (WasPressed(Keys.Down))
        {
            NavigateHistory(1);
        }
        else if (WasPressed(Keys.Escape))
        {
            // Safe to dispose from here now
            Dispose();
            return;
        }

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

    private void UpdateSuggestions(string text)
    {
        ClearSuggestions();

        if (string.IsNullOrWhiteSpace(text))
        {
            _suggestions.Visible = false;
            return;
        }

        var matches = _consoleHost.GetCompletions(text).Options.Take(MaxSuggestions).ToList();

        for (var i = 0; i < matches.Count; i++)
        {
            var index = i;
            var option = matches[i];

            var row = new SuggestionRow(option.Value, option.Hint, NormalColor, HintColor, HighlightBackground);
            row.OnClicked += () => ApplySuggestion(index);
            row.OnHovered += () => HighlightSuggestion(index);

            _suggestions.AddChild(row);
            _suggestionRows.Add(row);
            _suggestionValues.Add(option.Value);
        }

        _suggestions.Visible = _suggestionRows.Count > 0;

        if (_suggestionRows.Count > 0)
            HighlightSuggestion(0);
    }

    // moves the keyboard/hover highlight only
    private void HighlightSuggestion(int index)
    {
        if (_suggestionRows.Count == 0)
            return;

        _suggestionIndex = ((index % _suggestionRows.Count) + _suggestionRows.Count) % _suggestionRows.Count;

        for (var i = 0; i < _suggestionRows.Count; i++)
            _suggestionRows[i].Highlighted = i == _suggestionIndex;
    }

    private void ApplySuggestion(int index)
    {
        if (index < 0 || index >= _suggestionValues.Count)
            return;

        var text = _inputLine.Text;
        var lastSpace = text.LastIndexOf(' ');
        var head = lastSpace >= 0 ? text[..(lastSpace + 1)] : "";

        _inputLine.Text = head + _suggestionValues[index] + " ";
        _inputLine.MoveCaretToEnd();
        _ui.SetFocus(_inputLine); // a row click steals focus from the input - take it back
    }

    private void HideSuggestions()
    {
        ClearSuggestions();
        _suggestions.Visible = false;
    }

    private void ClearSuggestions()
    {
        foreach (var row in _suggestionRows)
            _suggestions.RemoveChild(row, dispose: true);

        _suggestionRows.Clear();
        _suggestionValues.Clear();
    }

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
        // only stick to the bottom if the user was already there
        var stickToBottom = IsScrolledToBottom();

        _lines.AddChild(new RichLabel { Text = Colored(text, color) });
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

    private static string Colored(string text, Color color) => $"[color=#{Hex(color)}]{Escape(text)}[/color]";

    private static string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string Escape(string text) => text.Replace("[", "[[").Replace("]", "]]");

    /// <summary>
    /// One row of the autocomplete dropdown
    /// </summary>
    private sealed class SuggestionRow : PanelContainer
    {
        public event Action? OnClicked;
        public event Action? OnHovered;

        private readonly Color _highlightBackground;
        private bool _highlighted;

        public bool Highlighted
        {
            get => _highlighted;
            set
            {
                _highlighted = value;
                Background = value ? _highlightBackground : null;
            }
        }

        public SuggestionRow(string value, string? hint, Color valueColor, Color hintColor, Color highlightBackground)
        {
            _highlightBackground = highlightBackground;
            MouseFilter = MouseFilterMode.Stop;
            Padding = new Thickness(6, 3, 6, 3);

            var markup = Colored(value, valueColor);
            if (hint is not null)
                markup += "  " + Colored(hint, hintColor);

            AddChild(new RichLabel { Text = markup });
        }

        protected internal override void MouseEntered()
        {
            base.MouseEntered();
            OnHovered?.Invoke();
        }

        protected internal override void Click(MouseButton button)
        {
            base.Click(button);
            OnClicked?.Invoke();
        }
    }
}
