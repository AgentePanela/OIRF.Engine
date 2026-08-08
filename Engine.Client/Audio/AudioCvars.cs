using Engine.Shared.Configuration;

namespace Engine.Client.Audio;

[CVarDefs]
public static class AudioCvars
{
    public static readonly CVarDef<float> MasterVolume =
        CVarDef.Create("audio.master-volume", 1f);
}
