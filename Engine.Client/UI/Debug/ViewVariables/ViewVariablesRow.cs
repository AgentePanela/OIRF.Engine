using System;
using Engine.Shared.Debug.ViewVariables;

namespace Engine.Client.UI.Debug.ViewVariables;

public sealed class ViewVariablesRow : BoxContainer
{
    public VVMemberInfo Member { get; }

    private readonly IViewVariablesAccess _access;
    private readonly VVEditorControl _editor;
    private readonly Button? _drillButton;
    private VVValue? _lastValue;

    public ViewVariablesRow(VVMemberInfo member, IViewVariablesAccess access, Action<string> setStatus)
    {
        Member = member;
        _access = access;

        Orientation = Orientation.Horizontal;
        Separation = 6;
        HorizontalExpand = true;

        AddChild(new Label
        {
            Text = member.CanWrite ? member.Name : Loc.GetString("engine-vv-member-read-only", ("name", member.Name)),
            MinWidth = 190,
            AutoWrap = false,
            TextVerticalAlign = VerticalAlignment.Center,
        });

        var ctx = new VVEditorContext { Access = access, Member = member, SetStatus = setStatus };
        _editor = VVEditorRegistry.Create(ctx);
        AddChild(_editor);

        if (member.Drillable)
        {
            _drillButton = new Button("»") { MinWidth = 28, Disabled = true };
            _drillButton.OnClick += _ => Drill();
            AddChild(_drillButton);
        }

        if (member.CanRemove)
        {
            var removeButton = new Button("-") { MinWidth = 28 };
            removeButton.OnClick += _ =>
            {
                if (access.TryRemoveAt(member.Path, out var error))
                    Parent?.RemoveChild(this, dispose: true);
                else
                    setStatus(error ?? Loc.GetString("engine-vv-fail-remove-element"));
            };
            AddChild(removeButton);
        }
    }

    /// <summary>
    /// Re-reads through this row's own access - a row of a server component does not read from the local world.
    /// </summary>
    public void Refresh()
    {
        _access.TryRead(Member.Path, out var value);
        Refresh(value);
    }

    public void Refresh(VVValue value)
    {
        _lastValue = value;
        _editor.Refresh(value);

        // not value.Local: a remote member has no live object, but it is still drillable by path
        if (_drillButton is not null)
            _drillButton.Disabled = value.Kind is VVValueKind.Null or VVValueKind.Error;
    }

    // EntityUid is a reference to a whole other entity, not a nested object - drilling into it
    // opens a window rooted at that entity instead of appending a member step
    private void Drill()
    {
        if (Member.Kind == VVValueKind.EntityRef)
        {
            // on the server side it travelled as a NetEntity - a local uid means nothing across processes
            if (Member.Path.Root.Side == VVSide.Server)
            {
                if (int.TryParse(_lastValue?.Text, out var netId) && netId > 0)
                    ViewVariablesWindow.Open(VVPath.Of(VVRoot.ServerEntity(new NetEntity(netId))));

                return;
            }

            if (_lastValue?.Local is EntityUid uid)
            {
                ViewVariablesWindow.OpenEntity(uid);
                return;
            }
        }

        ViewVariablesWindow.Open(Member.Path);
    }
}
