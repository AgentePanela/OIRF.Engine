using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Engine.Client.Scenes;
using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI.Debug;

/// <summary>
/// Search/select an entity, then browse its components
/// </summary>
public sealed class EntityDebugTab
{
    private const int MaxResults = 50;

    private readonly SceneManager _sceneManager;
    private readonly EntityManager _entManager;

    private readonly LineEdit _searchBox;
    private readonly ItemList _entityList;
    private readonly List<EntityUid> _entityRows = new();
    private readonly Label _entityInfo;

    private readonly ItemList _componentList;
    private readonly List<Component> _componentRows = new();
    private readonly BoxContainer _propPanel;
    private readonly Label _status;

    private EntityUid? _selectedUid;
    private bool _dirty = true;

    public Control Root { get; }

    public EntityDebugTab(SceneManager sceneManager, EntityManager entManager)
    {
        _sceneManager = sceneManager;
        _entManager = entManager;

        var root = new BoxContainer { Orientation = Orientation.Horizontal, _separation = 8 };
        Root = root;

        var left = new BoxContainer { Orientation = Orientation.Vertical, _separation = 6, MinWidth = 260 };
        root.AddChild(left);

        _searchBox = new LineEdit { PlaceholderText = "Search entity..." };
        _searchBox.OnTextChanged += _ => _dirty = true;
        left.AddChild(_searchBox);

        _entityList = new ItemList { MinHeight = 460 };
        _entityList.OnSelectionChanged += OnEntitySelected;
        left.AddChild(_entityList);

        var leftToolbar = new BoxContainer { Orientation = Orientation.Horizontal, _separation = 6 };
        var refreshBtn = new Button("Refresh");
        refreshBtn.OnClick += _ => RefreshEntityList();
        var deleteBtn = new Button("Delete");
        deleteBtn.OnClick += _ => DeleteSelectedEntity();
        leftToolbar.AddChild(refreshBtn);
        leftToolbar.AddChild(deleteBtn);
        left.AddChild(leftToolbar);

        var right = new BoxContainer { Orientation = Orientation.Vertical, _separation = 8, HorizontalExpand = true };
        root.AddChild(right);

        right.AddChild(new Label { Text = "Entity Inspector" });
        _entityInfo = new Label { Text = "No entity selected" };
        right.AddChild(_entityInfo);

        right.AddChild(new Label { Text = "Components" });
        _componentList = new ItemList { MinHeight = 150 };
        _componentList.OnSelectionChanged += OnComponentSelected;
        right.AddChild(_componentList);

        right.AddChild(new Label { Text = "Component Inspector (read-only)" });
        var scroll = new ScrollContainer { MinHeight = 240, HorizontalExpand = true };
        _propPanel = new BoxContainer { Orientation = Orientation.Vertical, _separation = 4, Margin = new(6) };
        scroll.AddChild(_propPanel);
        right.AddChild(scroll);

        _status = new Label { Text = "" };
        right.AddChild(_status);

        _sceneManager.OnSceneChanged += OnSceneChanged;
    }

    public void Dispose() => _sceneManager.OnSceneChanged -= OnSceneChanged;

    private void OnSceneChanged(Scene scene) => _dirty = true;

    public void Update(float dt)
    {
        if (_dirty && Root.EffectivelyVisible)
            RefreshEntityList();
    }

    private void RefreshEntityList()
    {
        _entityList.Clear();
        _entityRows.Clear();
        _dirty = false;

        var scene = _sceneManager.CurrentScene;
        if (scene is null)
        {
            _entityInfo.Text = "No current scene";
            return;
        }

        var filter = _searchBox.Text.Trim().ToLowerInvariant();
        if (filter.Length == 0)
        {
            _entityInfo.Text = $"Type to search ({scene.Entities.Count} entities)";
            return;
        }

        var count = 0;
        foreach (var kv in scene.Entities.OrderBy(k => k.Key.Id))
        {
            var uid = kv.Key;
            var ent = kv.Value;
            var display = string.IsNullOrWhiteSpace(ent.Name) ? $"Entity {uid.Id}" : $"{ent.Name} ({uid.Id})";

            if (!display.ToLowerInvariant().Contains(filter))
                continue;

            _entityRows.Add(uid);
            _entityList.AddItem(display);
            count++;

            if (count >= MaxResults)
                break;
        }

        _entityInfo.Text = count >= MaxResults
            ? $"Showing {MaxResults}+ results (refine your search)"
            : $"{count} result(s)";
    }

    private void DeleteSelectedEntity()
    {
        if (_selectedUid is not { } uid)
            return;

        _entManager.DeleteEntity(uid);
        _selectedUid = null;
        _dirty = true;

        _componentList.Clear();
        _componentRows.Clear();
        _propPanel.ClearChildren();
        _entityInfo.Text = "Entity deleted (or scheduled for deletion).";
    }

    private void OnEntitySelected(int index)
    {
        _componentList.Clear();
        _componentRows.Clear();
        _propPanel.ClearChildren();
        _status.Text = "";

        if (index < 0 || index >= _entityRows.Count)
        {
            _selectedUid = null;
            _entityInfo.Text = "Select an entity";
            return;
        }

        var uid = _entityRows[index];
        _selectedUid = uid;
        var ent = _entManager.GetEntity(uid);

        if (ent is null)
        {
            _entityInfo.Text = $"Entity ({uid.Id}) not found";
            return;
        }

        var comps = _entManager.GetEntityComps(uid);
        _entityInfo.Text = $"Name: {ent.Name}\nUID: {uid.Id}\nComponents: {comps?.Count ?? 0}";

        if (comps is null || comps.Count == 0)
        {
            _componentList.AddItem("<No components>");
            return;
        }

        foreach (var comp in comps)
        {
            _componentRows.Add(comp);
            _componentList.AddItem(comp.GetType().Name);
        }
    }

    private void OnComponentSelected(int index)
    {
        _propPanel.ClearChildren();
        _status.Text = "";

        if (index < 0 || index >= _componentRows.Count)
            return;

        BuildInspectorForComponent(_componentRows[index]);
    }

    private void BuildInspectorForComponent(Component comp)
    {
        var type = comp.GetType();
        _propPanel.AddChild(new Label { Text = type.FullName ?? type.Name });

        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name))
            AddMemberRow(p.Name, p.PropertyType.Name, p.CanRead ? TryGetValue(() => p.GetValue(comp)) : "<non-readable>");

        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name))
            AddMemberRow(f.Name, f.FieldType.Name, TryGetValue(() => f.GetValue(comp)));
    }

    private static string TryGetValue(Func<object?> getter)
    {
        try
        {
            return getter()?.ToString() ?? "null";
        }
        catch (Exception ex)
        {
            return $"<error: {ex.Message}>";
        }
    }

    private void AddMemberRow(string name, string typeName, string value)
    {
        _propPanel.AddChild(new Label { Text = $"{name} ({typeName}):" });
        _propPanel.AddChild(new Label { Text = value, Color = Color.Gray });
    }
}
