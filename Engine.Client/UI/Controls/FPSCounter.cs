using Apos.Shapes;
using Engine.Client.Debug.Diagnostics;
using Engine.Client.Graphics.Fonts;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

public class FPSCounter : Label
{
    private float _timer = 0f;
    private float _widthHighWater;

    public FPSCounter()
    {
        Text = "..."; // todo fix text = "" return vector2.zero
    }

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        var size = base.MeasureCore(availableSize);
        _widthHighWater = MathHelper.Max(_widthHighWater, size.X);
        return new Vector2(_widthHighWater, size.Y);
    }

    protected override void Update(float dt)
    {
        base.Update(dt);
        _timer += dt;
        if (_timer < 0.1f)
            return;

        _timer = _timer - 0.1f;
        Text = GameClient.GameTime.Fps.ToString();

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