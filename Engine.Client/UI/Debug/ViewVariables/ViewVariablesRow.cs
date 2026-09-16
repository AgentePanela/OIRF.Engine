using System;
using Engine.Shared.Debug.ViewVariables;

namespace Engine.Client.UI.Debug.ViewVariables;

public sealed class ViewVariablesRow : BoxContainer
{
    public VVMemberInfo Member { get; }

    private readonly VVEditorControl _editor;

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
    }

    public void Refresh(VVValue value) => _editor.Refresh(value);
}
