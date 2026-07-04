using Engine.Client.Assets;
using Engine.Client.Scenes.Factories;
using Engine.Client.Graphics;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.IoC;
using Engine.Client.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Engine.Shared.GameObjects.Factories;
using Engine.Shared.GameObjects;
using Engine.Shared.Assets;

namespace Engine.Client.Scenes;

/// <summary>
/// Handles game ENTIRE loading logic
/// </summary>
public class DefaultLoadingScene : LoadingScene
{
    private Label2D _label;
    private Vector2 _labelPos;
    private string _loadingFlavour = string.Empty;
    public override UICanvas? DefaultCanvas { get; protected set; } = null;

    public override void OnSceneStart()
    {
        base.OnSceneStart();

        _loadingFlavour = Loc.GetString("engine-loading-flavour-default");

        _labelPos = new Vector2(GameClient.Options.Width / 2, GameClient.Options.Height / 2);

        _label = new Label2D(
            TextStyle.Loading,
            _loadingFlavour,
            Vector2.Zero,
            0f,
            Vector2.One,
            Color.White,
            0f
        );

        _renderer.Resizing = false; // disable resizing for a better look
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        if (_state == LoadingState.Done)
            _loadingFlavour = Loc.GetString("engine-loading-flavour-done");

        _label.String = _loadingFlavour;
        _label.Origin = _textLayout.GetCenteredOrigin(_label);
    }

    protected override void TexturesPhase(float dt)
    {
        base.TexturesPhase(dt);
        var asset = IoCManager.Resolve<IAssetManager>() as AssetManager;

        _loadingFlavour = Loc.GetString("engine-loading-flavour-asset",
            ("textures", $"{asset?.initialPendingSprites - asset?._pending.Count}"), ("maxTextures", $"{asset?.initialPendingSprites}"));
    }

    protected override void RegistryPhase()
    {
        _loadingFlavour = Loc.GetString("engine-loading-flavour-registry");
        base.RegistryPhase();
    }

    public override void Draw(float dt)
    {
        base.Draw(dt);
        _renderer.Submit(_label, _labelPos);
    }

    protected override void LoadingCompleted()
    {
        base.LoadingCompleted();
        var ops = GameClient.Options;
        if (ops.InitialScene is not null)
        {
            if (!ops.InitialScene.IsSubclassOf(typeof(Scene)))
                throw new System.Exception("Initial scene is not a scene!");
            var ins = Activator.CreateInstance(ops.InitialScene) as Scene;
            if (ins is null)
                throw new NullReferenceException("Initial scene type instance is invalid/null.");
            _scene.ChangeScene(ins);
        }

        _renderer.Resizing = true;
    }
    
}
