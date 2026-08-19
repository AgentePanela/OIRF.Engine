using System;
using System.Collections.Generic;
using Engine.Client.Graphics.Fonts;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SpriteFontBase = FontStashSharp.SpriteFontBase;

namespace Engine.Client.UI;

public abstract partial class BaseTextInput : PanelContainer
{
    protected const float CaretBlinkInterval = 0.5f;

    protected static readonly RasterizerState ScissorRasterizer = new() { ScissorTestEnable = true };

    private string _text = "";

    /// <summary>
    /// The current text. Setting this updates the selection and fires <see cref="OnTextChanged"/>.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (_text == value)
                return;

            _text = value;
            _caret = Math.Clamp(_caret, 0, _text.Length);
            _selectionAnchor = Math.Clamp(_selectionAnchor, 0, _text.Length);
            InvalidateLayout(); // TextEdit's ArrangeCore re-measures scrollbar range off the new text
            OnTextChanged?.Invoke(_text);
        }
    }

    /// <summary>
    /// ghost text shown when theres no text inputed.
    /// </summary>
    public string PlaceholderText { get; set; } = "";

    /// <summary>
    /// Characters limit to the text length.
    /// </summary>
    public int MaxLength { get; set; } = int.MaxValue;

    private bool _readOnly;

    public bool ReadOnly
    {
        get => _readOnly;
        set
        {
            if (_readOnly == value)
                return;

            _readOnly = value;
            if (value)
                PseudoClasses.Add("readonly");
            else
                PseudoClasses.Remove("readonly");
        }
    }

    public event Action<string>? OnTextChanged;

    [StyleField("color", 0xFFFFFFFFu)]
    private Color? _color;

    [StyleField("placeholderColor", 0x757575FFu)]
    private Color? _placeholderColor;

    [StyleField("selectionColor", 0x3390FF80u)]
    private Color? _selectionColor;

    [StyleField("caretColor", 0xFFFFFFFFu)]
    private Color? _caretColor;

    [StyleField("caretWidth", 0.1f)]
    private float? _caretWidth;

    /// <summary>
    /// Caret height in pixels. 0 to match the current font height.
    /// </summary>
    [StyleField("caretHeight", 0f)]
    private float? _caretHeight;

    [StyleField("caretOffsetX", 0f)]
    private float? _caretOffsetX;

    [StyleField("caretOffsetY", 0f)]
    private float? _caretOffsetY;

    [StyleField("fontFamily")]
    private string? _fontFamily;

    [StyleField("fontSize", 16f)]
    private float? _fontSize;

    [StyleField("fontVariant", FontVariant.Regular)]
    private FontVariant? _fontVariant;

    protected int _caret;
    protected int _selectionAnchor;
    protected float _caretBlink;
    protected float _scrollPixels;

    protected internal override bool WantsVirtualKeyboard => true;

    protected BaseTextInput()
    {
        Focusable = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    protected SpriteFontBase ResolveFont(IFontManager fonts)
        => FontFamily is null ? fonts.Get(FontSize, FontVariant) : fonts.Get(FontSize, FontFamily, FontVariant);

    protected void ResetBlink() => _caretBlink = 0f;

    protected virtual void OnCaretChanged()
    {
    }

    protected void MoveCaret(int newCaret, bool extendSelection)
    {
        _caret = Math.Clamp(newCaret, 0, Text.Length);
        if (!extendSelection)
            _selectionAnchor = _caret;

        _coalescingTyping = false;
        _coalescingDeleting = false;
        OnCaretChanged();
    }

    protected void DeleteSelection()
    {
        if (_caret == _selectionAnchor)
            return;

        var start = Math.Min(_caret, _selectionAnchor);
        var end = Math.Max(_caret, _selectionAnchor);
        Text = Text.Remove(start, end - start);
        _caret = _selectionAnchor = start;
        OnCaretChanged();
    }

    #region copy/paste

    protected void CopySelection()
    {
        if (_caret == _selectionAnchor)
            return;

        var start = Math.Min(_caret, _selectionAnchor);
        var length = Math.Abs(_caret - _selectionAnchor);
        TextCopy.ClipboardService.SetText(Text.Substring(start, length));
    }

    protected void CutSelection()
    {
        CopySelection();
        DeleteSelection();
    }

    /// <summary>
    /// Deletes the selection (if any), then inserts at the caret, respecting MaxLength.
    /// </summary>
    protected void InsertText(string insert)
    {
        if (_caret != _selectionAnchor)
            DeleteSelection();

        var room = MaxLength - Text.Length;
        if (room <= 0)
            return;

        if (insert.Length > room)
            insert = insert[..room];

        Text = Text.Insert(_caret, insert);
        _caret += insert.Length;
        _selectionAnchor = _caret;
        OnCaretChanged();
    }

    #endregion
    #region Undo/Redo

    private readonly record struct UndoState(string Text, int Caret, int SelectionAnchor);
    private readonly Stack<UndoState> _undoStack = new();
    private readonly Stack<UndoState> _redoStack = new();
    protected bool _coalescingTyping;
    protected bool _coalescingDeleting;

    protected void PushUndo()
    {
        _undoStack.Push(new UndoState(Text, _caret, _selectionAnchor));
        _redoStack.Clear(); // a fresh edit invalidates whatever redo branch existed
    }

    protected void Undo()
    {
        if (ReadOnly || _undoStack.Count == 0)
            return;

        _redoStack.Push(new UndoState(Text, _caret, _selectionAnchor));
        var state = _undoStack.Pop();
        Text = state.Text;
        _caret = state.Caret;
        _selectionAnchor = state.SelectionAnchor;
        _coalescingTyping = false;
        _coalescingDeleting = false;
        OnCaretChanged();
        ResetBlink();
    }

    protected void Redo()
    {
        if (ReadOnly || _redoStack.Count == 0)
            return;

        _undoStack.Push(new UndoState(Text, _caret, _selectionAnchor));
        var state = _redoStack.Pop();
        Text = state.Text;
        _caret = state.Caret;
        _selectionAnchor = state.SelectionAnchor;
        _coalescingTyping = false;
        _coalescingDeleting = false;
        OnCaretChanged();
        ResetBlink();
    }

    #endregion
    #region Input (click)

    private long _lastClickTimeMs;
    private int _lastClickIndex = -1;
    protected int _clickCount;
    private const long MultiClickThresholdMs = 400;

    protected static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    protected static (int start, int end) FindWordBounds(string text, int index)
    {
        if (text.Length == 0)
            return (0, 0);

        var probe = Math.Clamp(index, 0, text.Length - 1);
        var isWord = IsWordChar(text[probe]);

        var start = probe;
        while (start > 0 && IsWordChar(text[start - 1]) == isWord)
            start--;

        var end = probe + 1;
        while (end < text.Length && IsWordChar(text[end]) == isWord)
            end++;

        return (start, end);
    }

    /// <summary>
    /// Finds the character index within a single line closest to a local X
    /// </summary>
    protected static int HitTestColumn(string line, float localX, SpriteFontBase font)
    {
        if (localX <= 0)
            return 0;

        var width = 0f;
        for (var i = 0; i < line.Length; i++)
        {
            var newWidth = width + font.MeasureString(line[i..(i + 1)]).X;
            if (newWidth >= localX)
                return localX - width < newWidth - localX ? i : i + 1;

            width = newWidth;
        }

        return line.Length;
    }

    protected int RegisterClick(int clickIndex)
    {
        var now = Environment.TickCount64;
        _clickCount = clickIndex == _lastClickIndex && now - _lastClickTimeMs <= MultiClickThresholdMs
            ? _clickCount % 3 + 1
            : 1;

        _lastClickTimeMs = now;
        _lastClickIndex = clickIndex;
        return _clickCount;
    }
    #endregion
}
