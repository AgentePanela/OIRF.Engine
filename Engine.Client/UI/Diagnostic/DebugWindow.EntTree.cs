#pragma warning disable CS0618

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Engine.Shared.GameObjects;
using Engine.Client.Scenes;
using Myra.Graphics2D.UI;
using MyraLabel = Myra.Graphics2D.UI.Label;
using MyraListBox = Myra.Graphics2D.UI.ListBox;

namespace Engine.Client.UI.Debug
{
    public sealed class EntityDebugTab : TabItem, IDisposable
    {
        private readonly SceneManager _sceneManager;
        private readonly EntityManager _entManager;
        private readonly Action<Scene> _onSceneChanged;

        private MyraListBox _entityList = default!;
        private MyraListBox _componentList = default!;
        private VerticalStackPanel _propPanel = default!;
        private MyraLabel _entityInfo = default!;
        private TextBox _searchBox = default!;
        private MyraLabel _status = default!;

        // mapping member -> control (kept for future use, but inspector is read-only now)
        private readonly Dictionary<MemberInfo, Widget> _memberControls = new();

        private EntityUid? _selectedUid = null;
        private Component? _selectedComponent = null;
        private bool _disposed;

        // dirty flag used for Update-based refresh
        private bool _dirty = true;

        public EntityDebugTab(SceneManager sceneManager, EntityManager entManager)
        {
            _sceneManager = sceneManager;
            _entManager = entManager;
            _onSceneChanged = (_) => _dirty = true;

            Text = "Entities";
            BuildUI();
            HookEvents();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _sceneManager.OnSceneChanged -= _onSceneChanged;
        }

        private void HookEvents()
        {
            _sceneManager.OnSceneChanged += _onSceneChanged;
        }

        private void BuildUI()
        {
            // Root: left column fixed, right column flexible (we make right column wide so inspector can use space)
            var root = new HorizontalStackPanel { Spacing = 8 };

            // LEFT SIDE - ENTITY LIST (fixed width)
            var leftPanel = new VerticalStackPanel
            {
                Width = 320,
                Spacing = 6
            };

            _searchBox = new TextBox { HintText = "Search entity..." };
            _searchBox.TextChanged += (_, __) => _dirty = true;

            _entityList = new MyraListBox
            {
                Width = 320,
                Height = 700
            };

            _entityList.SelectedIndexChanged += OnEntitySelected;

            var leftToolbar = new HorizontalStackPanel { Spacing = 6 };

            var refreshBtn = new Button
            {
                Content = new MyraLabel { Text = "Refresh" }
            };

            refreshBtn.Click += (_, __) => RefreshEntityList();

            var deleteBtn = new Button
            {
                Content = new MyraLabel { Text = "Delete" }
            };

            deleteBtn.Click += (_, __) => DeleteSelectedEntity();

            leftToolbar.Widgets.Add(refreshBtn);
            leftToolbar.Widgets.Add(deleteBtn);

            leftPanel.Widgets.Add(_searchBox);
            leftPanel.Widgets.Add(_entityList);
            leftPanel.Widgets.Add(leftToolbar);

            // RIGHT SIDE - will take the rest of the space (we give it a large width so controls don't overflow)
            var rightPanel = new VerticalStackPanel
            {
                Spacing = 10,
                Width = 1000 // intentionally large so inspector can expand; the layout system will clip if window is smaller
            };

            // ENTITY INSPECTOR (top)
            var entityInspector = new VerticalStackPanel
            {
                Spacing = 6
            };

            entityInspector.Widgets.Add(new MyraLabel
            {
                Text = "Entity Inspector"
            });

            _entityInfo = new MyraLabel
            {
                Text = "No entity selected"
            };

            _componentList = new MyraListBox
            {
                Height = 220
            };

            _componentList.SelectedIndexChanged += OnComponentSelected;

            entityInspector.Widgets.Add(_entityInfo);
            entityInspector.Widgets.Add(new MyraLabel { Text = "Components" });
            entityInspector.Widgets.Add(_componentList);

            // COMPONENT INSPECTOR (bottom) - read-only and occupies most vertical space
            var componentInspector = new VerticalStackPanel
            {
                Spacing = 6
            };

            componentInspector.Widgets.Add(new MyraLabel
            {
                Text = "Component Inspector"
            });

            _propPanel = new VerticalStackPanel
            {
                Spacing = 6,
                Width = 980 // keep slightly smaller than rightPanel width to avoid overflow
            };

            // ScrollViewer that allows the property list to expand vertically but remain inside the window
            var scroll = new ScrollViewer
            {
                Height = 580,
                Content = _propPanel
            };

            // Inspector toolbar: HIDDEN / disabled since inspector is read-only; show a status label instead
            var inspectorToolbar = new HorizontalStackPanel
            {
                Spacing = 6
            };

            // Hide apply/reload buttons because inspector is read-only now
            var readOnlyNotice = new MyraLabel { Text = "Read-only view" };

            _status = new MyraLabel { Text = "" };

            inspectorToolbar.Widgets.Add(readOnlyNotice);
            inspectorToolbar.Widgets.Add(_status);

            componentInspector.Widgets.Add(scroll);
            componentInspector.Widgets.Add(inspectorToolbar);

            // assemble right panel
            rightPanel.Widgets.Add(entityInspector);
            rightPanel.Widgets.Add(componentInspector);

            // ROOT
            root.Widgets.Add(leftPanel);
            root.Widgets.Add(rightPanel);

            Content = root;
        }

        private void DeleteSelectedEntity()
        {
            if (_selectedUid is null)
                return;

            _entManager.DeleteEntity(_selectedUid.Value);
            _selectedUid = null;
            _selectedComponent = null;
            _dirty = true;
            _componentList.Items.Clear();
            _propPanel.Widgets.Clear();
            _entityInfo.Text = "Entity deleted (or scheduled for deletion).";
        }

        private const int MaxResults = 50;

        private void RefreshEntityList()
        {
            _entityList.Items.Clear();
            _dirty = false;

            var scene = _sceneManager.CurrentScene;
            if (scene is null)
            {
                _entityInfo.Text = "No current scene";
                return;
            }

            var filter = _searchBox.Text?.Trim().ToLowerInvariant() ?? string.Empty;

            // Only populate when the user is actively searching.
            if (string.IsNullOrEmpty(filter))
            {
                _entityInfo.Text = $"Type to search ({scene.Entities.Count} entities)";
                return;
            }

            int count = 0;
            foreach (var kv in scene.Entities.OrderBy(k => k.Key.Id))
            {
                var uid = kv.Key;
                var ent = kv.Value;

                var display = string.IsNullOrWhiteSpace(ent.Name)
                    ? $"Entity {uid.Id}"
                    : $"{ent.Name} ({uid.Id})";

                if (!display.ToLowerInvariant().Contains(filter))
                    continue;

                _entityList.Items.Add(new ListItem(display) { Tag = uid });
                count++;

                if (count >= MaxResults)
                    break;
            }

            if (count >= MaxResults)
                _entityInfo.Text = $"Showing {MaxResults}+ results (refine your search)";
            else
                _entityInfo.Text = $"{count} result(s)";
        }

        private void OnEntitySelected(object? sender, EventArgs args)
        {
            _componentList.Items.Clear();
            _propPanel.Widgets.Clear();
            _memberControls.Clear();
            _selectedComponent = null;
            _status.Text = "";

            if (_entityList.SelectedItem?.Tag is not EntityUid uid)
            {
                _selectedUid = null;
                _entityInfo.Text = "Select an entity";
                return;
            }

            _selectedUid = uid;
            var ent = _entManager.GetEntity(uid);

            if (ent is null)
            {
                _entityInfo.Text = $"Entity ({uid.Id}) not found";
                return;
            }

            _entityInfo.Text =
$"Name: {ent.Name}\nUID: {uid.Id}\nComponents: {_entManager.GetEntityComps(uid)?.Count ?? 0}";

            var comps = _entManager.GetEntityComps(uid);

            if (comps is null || comps.Count == 0)
            {
                _componentList.Items.Add(new ListItem("<No components>"));
                return;
            }

            foreach (var comp in comps)
            {
                _componentList.Items.Add(new ListItem(comp.GetType().Name) { Tag = comp });
            }
        }

        private void OnComponentSelected(object? sender, EventArgs args)
        {
            _propPanel.Widgets.Clear();
            _memberControls.Clear();
            _selectedComponent = null;
            _status.Text = "";

            if (_componentList.SelectedItem?.Tag is not Component comp)
                return;

            _selectedComponent = comp;
            BuildInspectorForComponentReadOnly(comp);
        }

        private void BuildInspectorForComponentReadOnly(Component comp)
        {
            _propPanel.Widgets.Clear();
            _memberControls.Clear();

            var type = comp.GetType();
            _propPanel.Widgets.Add(new MyraLabel { Text = type.FullName ?? type.Name });

            // Properties (public instance)
            var props = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(p => p.Name);

            foreach (var p in props)
            {
                AddMemberReadOnlyForProperty(comp, p);
            }

            // Fields (public instance)
            var fields = type
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(f => f.Name);

            foreach (var f in fields)
            {
                AddMemberReadOnlyForField(comp, f);
            }
        }

        private void AddMemberReadOnlyForProperty(Component comp, PropertyInfo p)
        {
            var canRead = p.CanRead;
            var label = new MyraLabel { Text = $"{p.Name} ({p.PropertyType.Name}):" };
            _propPanel.Widgets.Add(label);

            if (!canRead)
            {
                _propPanel.Widgets.Add(new MyraLabel { Text = "<non-readable>" });
                return;
            }

            object? val;
            try
            {
                val = p.GetValue(comp);
            }
            catch (Exception ex)
            {
                val = $"<error: {ex.Message}>";
            }

            // Always show as readonly label
            var valueLabel = new MyraLabel { Text = val?.ToString() ?? "null", Width = 960, TextColor = Microsoft.Xna.Framework.Color.Gray };
            _propPanel.Widgets.Add(valueLabel);

            // keep mapping in case future features need it (e.g., copy-to-clipboard)
            _memberControls[p] = valueLabel;
        }

        private void AddMemberReadOnlyForField(Component comp, FieldInfo f)
        {
            var label = new MyraLabel { Text = $"{f.Name} ({f.FieldType.Name}):" };
            _propPanel.Widgets.Add(label);

            object? val;
            try
            {
                val = f.GetValue(comp);
            }
            catch (Exception ex)
            {
                val = $"<error: {ex.Message}>";
            }

            var valueLabel = new MyraLabel { Text = val?.ToString() ?? "null", Width = 960, TextColor = Microsoft.Xna.Framework.Color.Gray };
            _propPanel.Widgets.Add(valueLabel);

            _memberControls[f] = valueLabel;
        }

        private void ReloadSelectedComponentProperties()
        {
            if (_selectedComponent is null)
                return;

            // Rebuild inspector so controls reflect runtime values
            BuildInspectorForComponentReadOnly(_selectedComponent);
            _status.Text = "Reloaded.";
        }

        /// <summary>
        /// Called periodically by DebugWindow (forwarded from RootWindow.Update(float dt)).
        /// </summary>
        public void Update(float dt)
        {
            if (_dirty)
            {
                RefreshEntityList();
            }
        }
    }
}

#pragma warning restore CS0618