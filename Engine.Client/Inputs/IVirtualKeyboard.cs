namespace Engine.Client.Inputs;

/// <summary>
/// Platform hook for an on-screen keyboard.
/// </summary>
public interface IVirtualKeyboard
{
    void Show();
    void Hide();
}

/// <summary>
/// Does nothing award :clueless:
/// </summary>
public sealed class NullVirtualKeyboard : IVirtualKeyboard
{
    public void Show()
    {
    }

    public void Hide()
    {
    }
}
