using Apos.Shapes;
using Engine.Client.Debug.Diagnostics;
using Engine.Client.Graphics.Fonts;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

public class FPSCounter : Label
{
    private float _timer = 0f;
    public FPSCounter()
    {
        Text = "..."; // todo fix text = "" return vector2.zero
    }

    protected override void Update(float dt)
    {
        base.Update(dt);
        _timer += dt;
        if (_timer < 1f)
            return;

        _timer = 0f;
        Text = UIProfiler.LogSnapshot(true) + "\n" + MemoryMeter.GetInfo();

        switch (GameClient.GameTime.Fps)
        {
            case <= 10:
                Color = Color.Red;
                break;

            case <= 30:
                Color = Color.Yellow;
                break;
            
            case <= 45:
                Color = Color.Orange;
                break;
                
            default:
                Color = Color.White;
                break;
        }
    }
}