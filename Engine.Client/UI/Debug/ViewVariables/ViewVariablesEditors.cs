using System;
using System.Collections.Generic;
using System.Reflection;
using Engine.Shared.Debug.ViewVariables;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI.Debug.ViewVariables;

public sealed class VVEditorContext
{
    public required IViewVariablesAccess Access { get; init; }
    public required VVMemberInfo Member { get; init; }
    public required Action<string> SetStatus { get; init; }
}

/// <summary>
/// Base for every value editor. Handles the refresh-suppression rule  and the 
/// write/re-read/redisplay flow.
/// </summary>
public abstract class VVEditorControl : BoxContainer
{
    protected readonly VVEditorContext Ctx;
    private bool _userDirty;

    // true while ApplyValue is setting a control value programmatically
    protected bool ApplyingValue { get; private set; }

    protected VVEditorControl(VVEditorContext ctx)
    {
        Ctx = ctx;
        HorizontalExpand = true;
    }

    public void Refresh(VVValue value)
    {
        if (_userDirty || ContainsFocus())
            return;

        Apply(value);
    }

    private void Apply(VVValue value)
    {
        ApplyingValue = true;
        ApplyValue(value);
        ApplyingValue = false;
    }

    protected abstract void ApplyValue(VVValue value);

    protected void MarkDirty()
    {
        if (!ApplyingValue)
            _userDirty = true;
    }

    protected void TryCommit(object? parsed, string text)
    {
        if (!Ctx.Access.TryWrite(Ctx.Member.Path, parsed, text, out var error))
        {
            Ctx.SetStatus(error ?? "write failed");
            OnCommitFailed();
            return;
        }

        Ctx.SetStatus("");
        _userDirty = false;

        if (Ctx.Access.TryRead(Ctx.Member.Path, out var stored))
            Apply(stored);
    }

    protected virtual void OnCommitFailed()
    {
    }

    private bool ContainsFocus()
    {
        var focused = IoCManager.Resolve<UIManager>().FocusedControl;
        for (var c = focused; c is not null; c = c.Parent)
        {
            if (c == this)
                return true;
        }

        return false;
    }
}

public static class VVEditorRegistry
{
    private static Dictionary<Type, Func<VVEditorContext, VVEditorControl>>? _byType;

    private static Dictionary<Type, Func<VVEditorContext, VVEditorControl>> ByType => _byType ??= new()
    {
        [typeof(bool)] = ctx => new BoolEditor(ctx),
        [typeof(string)] = ctx => new StringEditor(ctx),
        [typeof(sbyte)] = ctx => new NumericEditor(ctx),
        [typeof(byte)] = ctx => new NumericEditor(ctx),
        [typeof(short)] = ctx => new NumericEditor(ctx),
        [typeof(ushort)] = ctx => new NumericEditor(ctx),
        [typeof(int)] = ctx => new NumericEditor(ctx),
        [typeof(uint)] = ctx => new NumericEditor(ctx),
        [typeof(long)] = ctx => new NumericEditor(ctx),
        [typeof(ulong)] = ctx => new NumericEditor(ctx),
        [typeof(float)] = ctx => new NumericEditor(ctx),
        [typeof(double)] = ctx => new NumericEditor(ctx),
        [typeof(decimal)] = ctx => new NumericEditor(ctx),
        [typeof(EntityUid)] = ctx => new EntityUidEditor(ctx),
        [typeof(Vector2)] = ctx => new Vector2Editor(ctx),
        [typeof(Vector3)] = ctx => new Vector3Editor(ctx),
        [typeof(Vector4)] = ctx => new Vector4Editor(ctx),
        [typeof(Color)] = ctx => new ColorEditor(ctx),
    };

    public static VVEditorControl Create(VVEditorContext ctx)
    {
        var type = ctx.Member.LocalType;

        // nothing to dispatch on - a member of a component only the other side has. The kind is still known, and a
        // remote write is parsed over there, so text is enough to keep the row editable
        if (type is null)
            return CreateForKind(ctx);

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return new NullableEditorDecorator(ctx, underlying);

        if (ByType.TryGetValue(type, out var factory))
            return factory(ctx);

        return CreateForType(ctx, type);
    }

    private static VVEditorControl CreateForKind(VVEditorContext ctx)
    {
        if (!ctx.Member.CanWrite)
            return new ReadOnlyTextEditor(ctx);

        if (ctx.Member.Kind == VVValueKind.Enum && ctx.Member.EnumNames is { Count: > 0 } names)
            return new NamedEnumEditor(ctx, names);

        return ctx.Member.Kind is VVValueKind.Scalar or VVValueKind.EntityRef
            ? new RawTextEditor(ctx)
            : new ReadOnlyTextEditor(ctx);
    }

    internal static VVEditorControl CreateForType(VVEditorContext ctx, Type type)
    {
        if (ByType.TryGetValue(type, out var factory))
            return factory(ctx);

        if (type.IsEnum)
        {
            return type.GetCustomAttribute<FlagsAttribute>() is not null
                ? new FlagsEnumEditor(ctx, type)
                : new EnumEditor(ctx, type);
        }

        return new ReadOnlyTextEditor(ctx);
    }
}
