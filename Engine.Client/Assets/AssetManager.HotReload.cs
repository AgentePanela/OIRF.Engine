using Engine.Shared.Assets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Engine.Client.Assets;

internal sealed partial class AssetManager : IAssetManager
{
    
    private ConcurrentQueue<string> _changedTextures = new();
    private List<FileSystemWatcher> _texWatcher = new();
    private void InitHotReload()
    {
        #if !DEBUG
        return; // nuh uh
        #endif
        var res = TexturesResPath.GetFolders();
        foreach (var dir in res)
        {
            var fWatcher = new FileSystemWatcher()
            {
                Path = Path.Combine(dir),
                Filter="*.png",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName |  
                            NotifyFilters.LastWrite |
                            NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _texWatcher.Add(fWatcher);
            fWatcher.Changed += OnChanged;
            fWatcher.Renamed += OnChanged;
        }
        
        // todo _texWatcher.Created += OnCreated;
    }

    private void HotReloadUpdate(GameTime? dt)
    {
        while (_changedTextures.TryDequeue(out var file))
            ReplaceTexture(file);

    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        var type = Path.GetExtension(e.FullPath);
        switch(type)
        {
            case ".png":
                _changedTextures.Enqueue(e.FullPath);
                break;
        }
    }

    private void ReplaceTexture(string path)
    {
        if (!_resMan.TryGetResFile(path, TexturesResPath, out var resFile))
            return;

        var key = resFile.Relative;

        if (!GetTexture(key, out var spr, out _))
            return;

        using var stream = File.OpenRead(path);
        var tex = Texture2D.FromStream(_graphics, stream);

        if (spr.Width != tex.Width || spr.Height != tex.Height)
        {
            _atlas.RemoveSpriteRef(key);
            _atlas.AddSprite(key, tex);
        }
        else
        {
            _atlas.ReplaceSprite(spr, tex);
        }

        tex.Dispose();

        Log.Debug($"{path} has changed, updating atlas.");
    }
}