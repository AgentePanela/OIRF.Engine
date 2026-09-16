using Microsoft.Xna.Framework;

namespace Engine.Client.UI.Debug.ViewVariables;

public sealed partial class ViewVariablesWindow
{
    private void Rebuild()
    {
        _body.ClearChildren();
        _rows.Clear();

        var snapshot = _access.Snapshot(_path);
        Title = string.IsNullOrEmpty(snapshot.Title) ? "View Variables" : $"View Variables - {snapshot.Title}";

        if (snapshot.Error is not null)
        {
            _statusLabel.Text = snapshot.Error;
            return;
        }

        _statusLabel.Text = _access.IsRemote ? "Remote VV is read-only for now." : "";

        foreach (var group in snapshot.Groups)
        {
            if (group.Name.Length > 0)
                _body.AddChild(new Label { Text = group.Name, Color = new Color(190, 190, 255) });

            foreach (var member in group.Members)
            {
                var row = new ViewVariablesRow(member, _access, text => _statusLabel.Text = text);
                _rows.Add(row);
                _body.AddChild(row);
            }
        }

        RefreshValues();
    }

    private void RefreshValues()
    {
        foreach (var row in _rows)
        {
            _access.TryRead(row.Member.Path, out var value);
            row.Refresh(value);
        }
    }
}
