using Engine.Client.Assets;
using Engine.Client.GameObjects;
using Engine.Shared.GameObjects;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using System;
using System.Runtime.CompilerServices;

namespace Engine.Client.Scenes;

/// <summary>
/// Handles all scenes lifetimes and transitioning.
/// </summary>
public sealed class SceneManager : DrawableGameComponent
{    
    [Dependency] private readonly EntityManager _entMan = default!;
    public Scene? CurrentScene {get; private set;}
    private Scene? _nextScene;
    public event Action<Scene>? OnSceneChanged;
    public event Action<Scene>? OnBeforeSceneInit;

    public SceneManager(Game? game) : base(game)
    {
        IoCManager.ResolveDependencies(this);
    }

    public override void Initialize()
    {
        base.Initialize();

        //if (CurrentScene is not null)
        //    CurrentScene.Initialize(default);
    }

    public override void Update(GameTime gameTime)
    {
        // Happens before current scene update
        if (_nextScene is not null)
            TransitionScene();
        else if (CurrentScene is null) //theres no scene
            ChangeScene(new DefaultLoadingScene());

        CurrentScene?.Update(GameClient.GameTime.DeltaTime);
        base.Update(gameTime);
    }

    public override void Draw(GameTime gameTime)
    {
        CurrentScene?.Draw(GameClient.GameTime.DeltaTime);
        base.Draw(gameTime);
    }

    /// <summary>
    /// Queue a scene from the disk to be changed in the next Update call.
    /// This will be called in the next update before the current scene update again.
    /// </summary>
    /// <param name="next">Scene id</param>
    public void ChangeScene(string sceneId)
    {
        Scene? next = null; //_sceneLoader.LoadSceneFromDisk(sceneId);
        if (next is null || CurrentScene == next)
            return;

        _nextScene = next;
    }

    /// <summary>
    /// Queue a scene to be changed in the next Update call.
    /// This will be called in the next update before the current scene update again.
    /// </summary>
    /// <param name="next">New scene</param>
    public void ChangeScene(Scene next)
    {
        if (CurrentScene == next)
            return;

        _nextScene = next;
    }

    private void TransitionScene()
    {
        // Dispose active scene.
        if (CurrentScene is not null)
            CurrentScene?.Dispose();

        // Force the garbage collector to collect to ensure memory is cleared.
        GC.Collect();

        // Change the currently active scene to the new scene.
        Log.Debug($"Changing current scene - {CurrentScene?.GetType().Name ?? "null"} > {_nextScene?.GetType().Name ?? "null"}");
        CurrentScene = _nextScene;
        _nextScene = null;

        if (CurrentScene is null)
            throw new Exception("Current scene is null during a scene transition!");
        
        OnBeforeSceneInit?.Invoke(CurrentScene!);
        _entMan.ForceScene(CurrentScene);
        CurrentScene?.Initialize();
        OnSceneChanged?.Invoke(CurrentScene!);
    }
}
