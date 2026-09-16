using System;
using Engine.Shared.Debug.ViewVariables;

namespace Engine.Client.UI.Debug.ViewVariables;

public sealed class ViewVariablesRow : BoxContainer
{
    public VVMemberInfo Member { get; }

    private readonly VVEditorControl _editor;
    private readonly Button? _drillButton;
    private VVValue? _lastValue;

    public ViewVariablesRow(VVMemberInfo member, IViewVariablesAccess access, Action<string> setStatus)
    {
        Member = member;

        Orientation = Orientation.Horizontal;
        Separation = 6;
        HorizontalExpand = true;

        AddChild(new Label
        {
            Text = member.CanWrite ? member.Name : $"{member.Name} (ro)",
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
    }

    public void Refresh(VVValue value)
    {
        _lastValue = value;
        _editor.Refresh(value);

        if (_drillButton is not null)
            _drillButton.Disabled = value.Local is null;
    }

    // EntityUid is a reference to a whole other entity, not a nested object - drilling into it
    // opens a window rooted at that entity instead of appending a member step
    private void Drill()
    {
        if (Member.Kind == VVValueKind.EntityRef && _lastValue?.Local is EntityUid uid)
            ViewVariablesWindow.OpenEntity(uid);
        else
            ViewVariablesWindow.Open(Member.Path);
    }
}
