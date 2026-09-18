using System.Collections.Generic;
using System.Linq;
using Engine.Client.Scenes;
using Engine.Client.UI.Debug.ViewVariables;
using Engine.Shared.GameObjects;

namespace Engine.Client.UI.Debug;

/// <summary>
/// Search/select an entity, then open it in vv
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
    private readonly Button _vvButton;

    private EntityUid? _selectedUid;
    private bool _dirty = true;

    public Control Root { get; }

    public EntityDebugTab(SceneManager sceneManager, EntityManager entManager)
    {
        _sceneManager = sceneManager;
        _entManager = entManager;

        var root = new BoxContainer { Orientation = Orientation.Vertical, Separation = 6, MinWidth = 260 };
        Root = root;

        _searchBox = new LineEdit { PlaceholderText = "Search entity..." };
        _searchBox.OnTextChanged += _ => _dirty = true;
        root.AddChild(_searchBox);

        _entityList = new ItemList { MinHeight = 460 };
        _entityList.OnSelectionChanged += OnEntitySelected;
        root.AddChild(_entityList);

        var toolbar = new BoxContainer { Orientation = Orientation.Horizontal, Separation = 6 };
        var refreshBtn = new Button("Refresh");
        refreshBtn.OnClick += _ => RefreshEntityList();
        var deleteBtn = new Button("Delete");
        deleteBtn.OnClick += _ => DeleteSelectedEntity();
        _vvButton = new Button("View Variables") { Disabled = true };
        _vvButton.OnClick += _ =>
        {
            if (_selectedUid is { } uid)
                ViewVariablesWindow.OpenEntity(uid);
        };
        toolbar.AddChild(refreshBtn);
        toolbar.AddChild(deleteBtn);
        toolbar.AddChild(_vvButton);
        root.AddChild(toolbar);

        _entityInfo = new Label { Text = "No entity selected" };
        root.AddChild(_entityInfo);

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

        var sceneEntities = _entManager.GetEntities();

        var filter = _searchBox.Text.Trim().ToLowerInvariant();
        if (filter.Length == 0)
        {
            _entityInfo.Text = $"Type to search ({sceneEntities.Count} entities)";
            return;
        }

        var count = 0;
        foreach (var uid in sceneEntities.OrderBy(u => u.Id))
        {
            var ent = _entManager.GetEntity(uid);
            if (ent is null)
                continue;

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

        _vvButton.Disabled = true;
        _entityInfo.Text = "Entity deleted (or scheduled for deletion).";
    }

    private void OnEntitySelected(int index)
    {
        if (index < 0 || index >= _entityRows.Count)
        {
            _selectedUid = null;
            _vvButton.Disabled = true;
            _entityInfo.Text = "Select an entity";
            return;
        }

        var uid = _entityRows[index];
        _selectedUid = uid;
        var ent = _entManager.GetEntity(uid);

        if (ent is null)
        {
            _vvButton.Disabled = true;
            _entityInfo.Text = $"Entity ({uid.Id}) not found";
            return;
        }

        var comps = _entManager.GetEntityComps(uid);
        _entityInfo.Text = $"Name: {ent.Name}\nUID: {uid.Id}\nComponents: {comps?.Count ?? 0}";
        _vvButton.Disabled = false;
    }
}
