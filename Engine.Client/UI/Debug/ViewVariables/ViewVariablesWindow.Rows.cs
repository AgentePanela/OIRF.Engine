using Engine.Shared.Debug.ViewVariables;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI.Debug.ViewVariables;

public sealed partial class ViewVariablesWindow
{
    private void Rebuild()
    {
        _body.ClearChildren();
        _componentsBody.ClearChildren();
        _rows.Clear();

        var snapshot = _access.Snapshot(_path);
        Title = string.IsNullOrEmpty(snapshot.Title) ? "View Variables" : $"View Variables - {snapshot.Title}";

        if (snapshot.Error is not null)
        {
            _statusLabel.Text = snapshot.Error;
            return;
        }

        _statusLabel.Text = _access.IsRemote ? "Remote VV is read-only for now." : "";

        var hasComponents = false;
        var componentIndex = 0;
        var memberIndex = 0;

        foreach (var group in snapshot.Groups)
        {
            // a nameless group with its own path and no members is a component entry - goes in
            // its own list, not mixed in with the entit own fields
            if (group.Members.Count == 0 && group.Path is { } compPath)
            {
                hasComponents = true;

                var row = new BoxContainer
                {
                    Orientation = Orientation.Horizontal,
                    Separation = 4,
                    HorizontalExpand = true,
                    Background = componentIndex++ % 2 == 0 ? new Color(0, 0, 0, 0.5f) : null, // odd have a darker backgroubnd :)
                };

                var openButton = new Button(group.Name) { HorizontalExpand = true };
                openButton.OnClick += _ => Open(compPath);
                row.AddChild(openButton);

                var removeButton = new Button("X") { MinWidth = 28 };
                removeButton.OnClick += _ =>
                {
                    if (IoCManager.Resolve<ViewVariablesManager>().TryRemoveComponent(compPath.Root, out var error))
                    {
                        _componentsBody.RemoveChild(row, dispose: true);
                        _statusLabel.Text = "";
                    }
                    else
                    {
                        _statusLabel.Text = error ?? "couldn't remove component";
                    }
                };
                row.AddChild(removeButton);

                _componentsBody.AddChild(row);
                continue;
            }

            if (group.Name.Length > 0)
                _body.AddChild(new Label { Text = group.Name, Color = new Color(190, 190, 255) });

            foreach (var member in group.Members)
            {
                var memberRow = new ViewVariablesRow(member, _access, text => _statusLabel.Text = text)
                {
                    Background = memberIndex++ % 2 == 0 ? new Color(0, 0, 0, 0.5f) : null,
                };
                _rows.Add(memberRow);
                _body.AddChild(memberRow);
            }
        }

        if (snapshot.Collection is { CanInsert: true } collection)
        {
            var addRow = new BoxContainer { Orientation = Orientation.Horizontal, Separation = 4, HorizontalExpand = true };

            LineEdit? keyEdit = null;
            if (collection.IsDictionary)
            {
                keyEdit = new LineEdit { PlaceholderText = "key", HorizontalExpand = true };
                addRow.AddChild(keyEdit);
            }

            var addButton = new Button("+") { HorizontalExpand = !collection.IsDictionary };
            addButton.OnClick += _ =>
            {
                if (_access.TryInsert(_path, keyEdit?.Text, out var error))
                    Rebuild();
                else
                    _statusLabel.Text = error ?? "couldn't add element";
            };
            addRow.AddChild(addButton);

            _body.AddChild(addRow);
        }

        _componentsLabel.Visible = hasComponents;
        _componentsScroll.Visible = hasComponents;

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
