using Engine.Client.Assets.Atlas;
using Engine.Client.Graphics;
using Engine.Shared.Assets;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace Engine.Client.Assets;

/// <summary>
/// Manages the Resources/ loading.
/// </summary>
internal sealed partial class AssetManager : IAssetManager
{
    //[Dependency] private SceneLoader _sceneLoader = default!;
    [Dependency] private readonly SharedResourceManager _resMan = default!;
    public ResPath TexturesResPath { get; private set; } = new ("Textures");
    private GraphicsDevice _graphics = default!;
    private readonly AtlasBuilder _atlas = default!;
    internal Stack<(string key, byte[] data)> _pending = new();
    internal int initialPendingSprites { get; private set; } = 0;
    private Stopwatch _sw = default!;

    public event Action? OnLoadingCompleted;

    public AssetManager()
    {
        _atlas = new(GameClient.Options.CreateDedicatedAtlas);
        IoCManager.ResolveDependencies(this);
    }

    bool IAssetManager.Init(GraphicsDevice device, SpriteBatch spriteBatch)
    {
        _graphics = device;
        var res = SharedResourceManager.GetMainResourcesFolder();
        Log.Debug($"Initing AssetManager... - Main resources folder in {res}");

        _sw = Stopwatch.StartNew();
        //_sceneLoader.Init(res);
        _atlas.Init(device, spriteBatch);

        Log.Debug("Loading raw textures...");
        //LoadTextures(res);
        _pending = LoadRawTextures();
        initialPendingSprites = _pending.Count;
        Log.Debug($"Raw texture completed! Took: {_sw.Elapsed}.");

#if DEBUG
        InitHotReload();
#endif
        return true;
    }

    void IAssetManager.Update(GameTime? dt)
    {
        if (GameClient.GameState != GameState.Running)
            return;

#if DEBUG
        HotReloadUpdate(dt);
#endif
    }

    void IAssetManager.UpdateLoading(GameTime? dt)
    {
        if (_pending.Count == 0)
        {
            Log.Debug($"Sprites stream completed! Took: {_sw.Elapsed} (total).");
            _atlas.BakeAll();
            GC.Collect();
            _sw.Stop();
            Log.Debug($"Asset loading completed! Took: {_sw.Elapsed}.");
            OnLoadingCompleted?.Invoke();
            return;
        }
        UploadTextures(30);
    }

    internal Stack<(string key, byte[] data)> LoadRawTextures()
    {
        LoadAnimationInfo();

        var list = new Stack<(string, byte[])>();
        var files = TexturesResPath.GetFiles("png");

        foreach (var resFile in files)
        {
            //Debug.WriteLine($"Reading {key}.png");

            // spritesheet source: keep its natural key, gets sliced in UploadTextures instead of queued as-is
            if (_sheetSources.ContainsKey(resFile.Relative))
            {
                var sheetBytes = File.ReadAllBytes(resFile.FilePath);
                list.Push((resFile.Relative, sheetBytes));
                continue;
            }

            // loose frame file explicitly listed in an animation's `files:` -> queue under the remapped frame key
            var key = _explicitFrames.TryGetValue(resFile.Relative, out var remapped)
                ? remapped
                : resFile.Relative;

            var bytes = File.ReadAllBytes(resFile.FilePath);
            list.Push((key, bytes));
        }

        return list;
    }

    internal void UploadTextures(int maxPerFrame)
    {
        string curKey = "Nothing!";
        for (int i = 0; i < maxPerFrame && _pending.Count > 0; i++)
        {
            try
            {
                var (key, data) = _pending.Pop();
                curKey = key;

                using var ms = new MemoryStream(data);
                var texture = Texture2D.FromStream(_graphics, ms);
                //Debug.WriteLine($"Streaming {key}");

                if (_sheetSources.TryGetValue(key, out var slice))
                {
                    SliceAndQueue(texture, slice);
                    texture.Dispose();
                }
                else
                {
                    _atlas.QueueSprite(key, texture);
                }
            }
            catch
            {
                Console.BackgroundColor = ConsoleColor.Red;
                Log.Error($"Something went wrong loading {curKey}");
                Console.ResetColor();
            }
        }
    }

    [Obsolete]
    internal void LoadTextures(string root)
    {
        var files = Directory.GetFiles(
            root,
            "*.png",
            SearchOption.AllDirectories
        );

        foreach (var file in files)
        {
            try
            {
                var key = NormalizeKey(root, file);
                Log.Debug($"Loading texture {key}.png");
                using var stream = File.OpenRead(file);
                var texture = Texture2D.FromStream(_graphics, stream);

                _atlas.QueueSprite(key, texture);
            }
            catch
            {
                Log.Error($"Something went wrong loading {file}");
            }
        }
        _atlas.BakeAll();

        Log.Debug($"Loaded {_atlas.sprites.Count} textures into {_atlas.pages.Count} atlas(ses)?");
    }

    [Obsolete]
    public string NormalizeKey(string root, string fullPath)
    {
        var relative = Path.GetRelativePath(root, fullPath);
        return Path.ChangeExtension(relative, null)
                   .Replace('\\', '/');
    }

    [Obsolete]
    public string GetResourcesFolder()
        => SharedResourceManager.GetMainResourcesFolder();


    public bool GetTexture(string key, [NotNullWhen(true)] out AtlasSprite sprite, [NotNullWhen(true)] out AtlasPage page)
    {
        sprite = _atlas.sprites["EngineInternal/Placeholders/Null"]; // invalid sprite
        page = _atlas.pages[sprite.Page];

        if (!_atlas.sprites.ContainsKey(key))
            return false;

        sprite = _atlas.sprites[key];
        page = _atlas.pages[sprite.Page];
        return true;
    }

    public bool GetSprite(string key, [NotNullWhen(true)]out AtlasSprite sprite, [NotNullWhen(true)]out AtlasPage page)
    {
        if (_atlas.sprites.TryGetValue(key, out sprite))
        {
            page = _atlas.pages[sprite.Page];
            return true;
        }

        // fallback to invalid sprite
        sprite = _atlas.sprites["EngineInternal/Placeholders/Null"];
        page = _atlas.pages[sprite.Page];
        return false;
    }

    public bool HasSprite(string key)
    {
        return _atlas.sprites.ContainsKey(key);
    }

    private Sprite2D GetInvalidSprite()
    {
        var key = "EngineInternal/Placeholders/Null";
        var aSpr = _atlas.sprites[key];
        var sprite = new Sprite2D(key, aSpr.Width, aSpr.Height);
        return sprite;
    }

    public bool GetSprite(string key, [NotNullWhen(true)] out Sprite2D sprite)
    {
        if (_atlas.sprites.TryGetValue(key, out var aSpr))
        {
            sprite = new Sprite2D(key, aSpr.Width, aSpr.Height);
            return true;
        }

        // bare animation key (e.g. "Player/walk-anim") used without an AnimationComponent driving it -> show frame 0
        if (_animations.TryGetValue(key, out var anim))
            return GetSprite(anim.FrameKey(0), out sprite);

        sprite = GetInvalidSprite();
        Log.Warn($"Sprite {key} does not exist!");
        return false;
    }

    public Sprite2D AddSprite(TextureRect texture, string key)
    {
        _atlas.AddSprite(key, texture.Texture);
        var aSpr = _atlas.sprites[key];
        var spr = new Sprite2D(key, aSpr.Width, aSpr.Height);
        spr.Layer = texture.Layer;
        spr.SamplerState = texture.SamplerState;
        spr.Offset = texture.Offset;
        spr.Origin = texture.Origin;
        spr.Rotation = texture.Rotation;
        spr.Scale = texture.Scale;
        spr.Color = texture.Color;
        spr.Depth = texture.Depth;
        spr.Visible = texture.Visible;

        return spr;
    }

    public void RemoveSprite(string key)
    {
        _atlas.RemoveSpriteRef(key);
    }

    public List<AtlasPage> GetAllAtlasses()
    {
        return _atlas.pages;
    }
}