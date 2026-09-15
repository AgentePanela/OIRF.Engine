using Engine.Shared.Debug.ViewVariables;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI.Debug.ViewVariables;

/// <summary>
/// One name/value row in a VV window.
/// </summary>
public sealed class ViewVariablesRow : BoxContainer
{
    public VVMemberInfo Member { get; }

    private readonly Label _valueLabel;

    public ViewVariablesRow(VVMemberInfo member)
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

        _valueLabel = new Label { HorizontalExpand = true, AutoWrap = false };
        AddChild(_valueLabel);
    }

    public void Refresh(VVValue value)
    {
        _valueLabel.Text = value.Text;
        _valueLabel.Color = value.Kind == VVValueKind.Error ? Color.IndianRed : Color.LightGray;
    }
}
