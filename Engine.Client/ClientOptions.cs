using System;
using System.IO;
using System.Reflection;
using Engine.Client.Assets.Atlas;
using Engine.Client.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client;

public sealed class ClientOptions
{
    public string Version = "1.0.0";
    public string Title = "My Game";
    public int Width = 1280;
    public int Height = 720;
    public bool FullScreen = false;
    public Color BackgroundColor = Color.Black;

    /// <summary>
    /// Will create a dedicated atlas page if a sprite is too big for a atlas?
    /// </summary>
    public bool CreateDedicatedAtlas = true;

    public bool WindowResizing = true;

    public SamplerState Samplimg = SamplerState.PointClamp;

    /// <summary>
    /// The main scene the game will load when it get loaded.
    /// </summary>
    public Type? InitialScene;

    /// <summary>
    /// The default loading scene used by the engine.
    /// </summary>
    public Type LoadingScene = typeof(DefaultLoadingScene);

    /// <summary>
    /// The %AppData% location for your game data storage.
    /// </summary>
    public string DataPath = Path.Combine("MyCompany", "MyGame");
    
    /// <summary>
    /// Will save the current CVar config when the game closes?
    /// </summary>
    public bool SaveConfigOnExit = false;

    /// <summary>
    /// <strong>EXPERIMENTAL</strong> -
    /// Stop all game (and engine) update logic calls when focus loosed.
    /// </summary>
    public bool PauseOnUnfocus = false;

    /// <summary>
    /// Set the texture atlas size. This decision must be make having the game default texture size in mind.
    /// </summary>
    public AtlasSize TextureAtlasSize = AtlasSize.Size2048;

    public string[] Args = [];

    public Assembly[] Assemblies = [];
}
