using System.Collections.Generic;
using Engine.Shared.Debug.ViewVariables;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI.Debug.ViewVariables;

public sealed partial class ViewVariablesWindow
{
    // one tab page: everything that belongs to a single side of the wire
    private sealed class SideView
    {
        public readonly string Title;
        public readonly VVPath Path;
        public readonly IViewVariablesAccess Access;

        public readonly BoxContainer Root;
        public readonly BoxContainer Body;
        public readonly Label ComponentsLabel;
        public readonly ScrollContainer ComponentsScroll;
        public readonly BoxContainer ComponentsBody;
        public readonly List<ViewVariablesRow> Rows = new();

        public int Structure;
        public string SnapshotTitle = "";

        public SideView(string title, VVPath path, IViewVariablesAccess access, ViewVariablesWindow window)
        {
            Title = title;
            Path = path;
            Access = access;

            Root = new BoxContainer { Orientation = Orientation.Vertical, Separation = 6 };
            Root.AddChild(new Label { Text = path.ToString(), AutoWrap = false });

            var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, MinHeight = 200 };
            Root.AddChild(scroll);

            Body = new BoxContainer { Orientation = Orientation.Vertical, Separation = 4 };
            scroll.AddChild(Body);

            ComponentsLabel = new Label { Text = "Components:", Visible = false };
            Root.AddChild(ComponentsLabel);

            ComponentsScroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, MinHeight = 120, Visible = false };
            Root.AddChild(ComponentsScroll);

            ComponentsBody = new BoxContainer { Orientation = Orientation.Vertical, Separation = 4, Background = new (0, 0, 0, 0.5f) };
            ComponentsScroll.AddChild(ComponentsBody);

            if (path.Root.Kind != VVRootKind.Entity)
                return;

            var addButton = new Button("Add Component");
            addButton.OnClick += _ => window.ToggleAddComponentPopup(addButton, this);
            Root.AddChild(addButton);
        }
    }

    private void Rebuild(SideView side)
    {
        side.Body.ClearChildren();
        side.ComponentsBody.ClearChildren();
        side.Rows.Clear();

        var snapshot = side.Access.Snapshot(side.Path);
        side.Structure = snapshot.StructureVersion;

        side.SnapshotTitle = snapshot.Title;
        ApplyTitle();

        if (snapshot.Error is not null)
        {
            side.Body.AddChild(new Label { Text = snapshot.Error, Color = new Color(190, 190, 255) });
            side.ComponentsLabel.Visible = false;
            side.ComponentsScroll.Visible = false;
            return;
        }

        var hasComponents = BuildGroups(side, snapshot);
        BuildCollectionFooter(side, snapshot);

        side.ComponentsLabel.Visible = hasComponents;
        side.ComponentsScroll.Visible = hasComponents;

        foreach (var row in side.Rows)
            row.Refresh();
    }

    private bool BuildGroups(SideView side, VVSnapshot snapshot)
    {
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
                side.ComponentsBody.AddChild(BuildComponentRow(side, group.Name, compPath, componentIndex++));
                continue;
            }

            if (group.Name.Length > 0)
                side.Body.AddChild(new Label { Text = group.Name, Color = new Color(190, 190, 255) });

            foreach (var member in group.Members)
            {
                var memberRow = new ViewVariablesRow(member, side.Access, text => _statusLabel.Text = text)
                {
                    Background = memberIndex++ % 2 == 0 ? new Color(0, 0, 0, 0.5f) : null,
                };
                side.Rows.Add(memberRow);
                side.Body.AddChild(memberRow);
            }
        }

        return hasComponents;
    }

    private BoxContainer BuildComponentRow(SideView side, string name, VVPath compPath, int index)
    {
        var row = new BoxContainer
        {
            Orientation = Orientation.Horizontal,
            Separation = 4,
            HorizontalExpand = true,
            Background = index % 2 == 0 ? new Color(0, 0, 0, 0.5f) : null, // odd have a darker backgroubnd :)
        };

        var openButton = new Button(name) { HorizontalExpand = true };
        openButton.OnClick += _ => Open(compPath);
        row.AddChild(openButton);

        var removeButton = new Button("X") { MinWidth = 28 };
        removeButton.OnClick += _ =>
        {
            if (side.Access.TryRemoveComponent(compPath.Root, out var error))
            {
                side.ComponentsBody.RemoveChild(row, dispose: true);
                _statusLabel.Text = "";
            }
            else
            {
                _statusLabel.Text = error ?? "couldn't remove component";
            }
        };
        row.AddChild(removeButton);

        return row;
    }

    private void BuildCollectionFooter(SideView side, VVSnapshot snapshot)
    {
        if (snapshot.Collection is not { CanInsert: true } collection)
            return;

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
            if (side.Access.TryInsert(side.Path, keyEdit?.Text, out var error))
                Rebuild(side);
            else
                _statusLabel.Text = error ?? "couldn't add element";
        };
        addRow.AddChild(addButton);

        side.Body.AddChild(addRow);
    }
}
