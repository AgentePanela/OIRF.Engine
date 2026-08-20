using System;
using Engine.Client.Inputs;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

public partial class Window
{
    [Flags]
    private enum ResizeEdge
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8,
    }

    private enum DragMode
    {
        None,
        Move,
        Resize,
    }

    private DragMode _mode;
    private ResizeEdge _edge;
    private Vector2 _dragStartMouse;
    private Vector2 _dragStartPosition;
    private Vector2 _dragStartSize;

    protected internal override void MouseButtonDown(MouseButton button)
    {
        base.MouseButtonDown(button);
        Manager?.BringToFront(this);

        if (button != MouseButton.Left)
            return;

        var mouse = IoCManager.Resolve<InputManager>().MouseScreenPosition;
        _dragStartMouse = mouse;
        _dragStartPosition = new Vector2(Bounds.X, Bounds.Y);
        _dragStartSize = new Vector2(Bounds.Width, Bounds.Height);

        LayoutContainer.SetPosition(this, _dragStartPosition);
        LayoutContainer.SetSize(this, _dragStartSize);
        _edge = Resizable ? EdgeAt(mouse) : ResizeEdge.None;
        if (_edge != ResizeEdge.None)
        {
            _mode = DragMode.Resize;
            PseudoClasses.Add("resizing");
            return;
        }

        if (_titleRow.Bounds.Contains(mouse.ToPoint()))
        {
            _mode = DragMode.Move;
            PseudoClasses.Add("dragging");
        }
    }

    protected internal override void MouseMove(Vector2 position)
    {
        if (_mode == DragMode.None)
            return;

        var delta = position - _dragStartMouse;

        if (_mode == DragMode.Move)
        {
            LayoutContainer.SetPosition(this, _dragStartPosition + delta);
            return;
        }

        var x = _dragStartPosition.X;
        var y = _dragStartPosition.Y;
        var width = _dragStartSize.X;
        var height = _dragStartSize.Y;
        if (_edge.HasFlag(ResizeEdge.Left))
        {
            var dx = MathHelper.Min(delta.X, width - MinWidth);
            x += dx;
            width -= dx;
        }
        else if (_edge.HasFlag(ResizeEdge.Right))
        {
            width = MathHelper.Max(MinWidth, width + delta.X);
        }

        if (_edge.HasFlag(ResizeEdge.Top))
        {
            var dy = MathHelper.Min(delta.Y, height - MinHeight);
            y += dy;
            height -= dy;
        }
        else if (_edge.HasFlag(ResizeEdge.Bottom))
        {
            height = MathHelper.Max(MinHeight, height + delta.Y);
        }

        LayoutContainer.SetPosition(this, new Vector2(x, y));
        LayoutContainer.SetSize(this, new Vector2(width, height));
    }

    protected internal override void MouseButtonUp(MouseButton button)
    {
        base.MouseButtonUp(button);

        if (button != MouseButton.Left)
            return;

        _mode = DragMode.None;
        _edge = ResizeEdge.None;
        PseudoClasses.Remove("dragging");
        PseudoClasses.Remove("resizing");
    }

    protected internal override CursorShape GetCursorShape(Vector2 point)
    {
        var edge = _mode == DragMode.Resize ? _edge : Resizable ? EdgeAt(point) : ResizeEdge.None;
        return edge switch
        {
            ResizeEdge.Left or ResizeEdge.Right => CursorShape.SizeWE,
            ResizeEdge.Top or ResizeEdge.Bottom => CursorShape.SizeNS,
            ResizeEdge.Left | ResizeEdge.Top or ResizeEdge.Right | ResizeEdge.Bottom => CursorShape.SizeNWSE,
            ResizeEdge.Right | ResizeEdge.Top or ResizeEdge.Left | ResizeEdge.Bottom => CursorShape.SizeNESW,
            _ => CursorShape.Arrow,
        };
    }

    private ResizeEdge EdgeAt(Vector2 point)
    {
        var border = ResizeBorder;
        var edge = ResizeEdge.None;

        if (point.X - Bounds.Left <= border)
            edge |= ResizeEdge.Left;
        else if (Bounds.Right - point.X <= border)
            edge |= ResizeEdge.Right;

        if (point.Y - Bounds.Top <= border)
            edge |= ResizeEdge.Top;
        else if (Bounds.Bottom - point.Y <= border)
            edge |= ResizeEdge.Bottom;

        return edge;
    }
}
