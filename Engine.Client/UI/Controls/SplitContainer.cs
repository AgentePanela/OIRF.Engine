using System;
using Engine.Client.Inputs;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

/// <summary>
/// Two children with a draggable divider between them.
/// </summary>
public sealed partial class SplitContainer : Control
{
    private Orientation _orientation = Orientation.Horizontal;

    public Orientation Orientation
    {
        get => _orientation;
        set => SetLayoutField(ref _orientation, value);
    }

    [StyleField("splitterThickness", 6f)]
    private float? _splitterThickness;

    // -1 means that will defaults to a 50/50 split on first arrange.
    private float _splitOffset = -1f;

    /// <summary>
    /// Divider position in pixels from the start (left or top).
    /// </summary>
    public float SplitOffset
    {
        get => _splitOffset;
        set => SetLayoutField(ref _splitOffset, value);
    }

    private readonly Handle _handle;
    private Control? _first;
    private Control? _second;
    private float _lastOffset;
    private bool _dragging;
    private float _dragStartOffset;
    private Vector2 _dragStartMouse;

    public SplitContainer()
    {
        StyleAliasses.Add("splitContainer");

        _handle = new Handle();
        _handle.StyleAliasses.Add("splitHandle");
        _handle.OnDragStart += pos =>
        {
            _dragging = true;
            _dragStartMouse = pos;
            _dragStartOffset = _lastOffset;
        };
        _handle.OnDrag += pos =>
        {
            if (!_dragging)
                return;

            var delta = Orientation == Orientation.Horizontal ? pos.X - _dragStartMouse.X : pos.Y - _dragStartMouse.Y;
            SplitOffset = _dragStartOffset + delta;
        };
        _handle.OnDragEnd += () => _dragging = false;

        base.AddChild(_handle);
    }

    public new void AddChild(Control child)
    {
        if (_first is null)
        {
            _first = child;
            base.AddChild(child);
        }
        else if (_second is null)
        {
            _second = child;
            base.AddChild(child);
        }
        else
        {
            throw new InvalidOperationException("SplitContainer already has both children.");
        }
    }

    public new void RemoveChild(Control child, bool dispose = false)
    {
        if (child == _first)
            _first = null;
        else if (child == _second)
            _second = null;

        base.RemoveChild(child, dispose);
    }

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        _handle.Measure(availableSize);
        _first?.Measure(availableSize);
        _second?.Measure(availableSize);

        var firstSize = _first?.DesiredSize ?? Vector2.Zero;
        var secondSize = _second?.DesiredSize ?? Vector2.Zero;

        return Orientation == Orientation.Horizontal
            ? new Vector2(firstSize.X + SplitterThickness + secondSize.X, MathHelper.Max(firstSize.Y, secondSize.Y))
            : new Vector2(MathHelper.Max(firstSize.X, secondSize.X), firstSize.Y + SplitterThickness + secondSize.Y);
    }

    protected override void ArrangeCore(Rectangle finalRect)
    {
        var horizontal = Orientation == Orientation.Horizontal;
        var total = horizontal ? finalRect.Width : finalRect.Height;

        var minFirst = horizontal ? (_first?.MinWidth ?? 0f) : (_first?.MinHeight ?? 0f);
        var minSecond = horizontal ? (_second?.MinWidth ?? 0f) : (_second?.MinHeight ?? 0f);

        var offset = SplitOffset >= 0f ? SplitOffset : (total - SplitterThickness) / 2f;
        offset = MathHelper.Clamp(offset, minFirst, MathHelper.Max(minFirst, total - SplitterThickness - minSecond));
        _lastOffset = offset;

        _handle.Orientation = Orientation;

        if (horizontal)
        {
            var handleRect = new Rectangle(finalRect.X + (int)offset, finalRect.Y, (int)SplitterThickness, finalRect.Height);
            _handle.Arrange(handleRect);
            _first?.Arrange(new Rectangle(finalRect.X, finalRect.Y, (int)offset, finalRect.Height));
            _second?.Arrange(new Rectangle(handleRect.Right, finalRect.Y, finalRect.Right - handleRect.Right, finalRect.Height));
        }
        else
        {
            var handleRect = new Rectangle(finalRect.X, finalRect.Y + (int)offset, finalRect.Width, (int)SplitterThickness);
            _handle.Arrange(handleRect);
            _first?.Arrange(new Rectangle(finalRect.X, finalRect.Y, finalRect.Width, (int)offset));
            _second?.Arrange(new Rectangle(finalRect.X, handleRect.Bottom, finalRect.Width, finalRect.Bottom - handleRect.Bottom));
        }
    }

    private sealed class Handle : PanelContainer
    {
        public Orientation Orientation;
        public event Action<Vector2>? OnDragStart;
        public event Action<Vector2>? OnDrag;
        public event Action? OnDragEnd;

        public Handle() => MouseFilter = MouseFilterMode.Stop;

        protected internal override void MouseButtonDown(MouseButton button)
        {
            base.MouseButtonDown(button);
            if (button == MouseButton.Left)
                OnDragStart?.Invoke(IoCManager.Resolve<InputManager>().MouseScreenPosition);
        }

        protected internal override void MouseMove(Vector2 position) => OnDrag?.Invoke(position);

        protected internal override void MouseButtonUp(MouseButton button)
        {
            base.MouseButtonUp(button);
            if (button == MouseButton.Left)
                OnDragEnd?.Invoke();
        }

        protected internal override CursorShape GetCursorShape(Vector2 point)
            => Orientation == Orientation.Horizontal ? CursorShape.SizeWE : CursorShape.SizeNS;
    }
}
