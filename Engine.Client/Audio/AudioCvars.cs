using Engine.Shared.Configuration;

namespace Engine.Client.Audio;

[CVarDefs]
public static class AudioCvars
{
    public static readonly CVarDef<float> MasterVolume =
        CVarDef.Create("audio.master-volume", 1f);

    /// <summary>
    /// When false the AudioSystem starts no playback at all. Sounds already playing keep going.
    /// </summary>
    public static readonly CVarDef<bool> SystemPaused =
        CVarDef.Create("audio.system-paused", true, CVar.NOSAVE);
}
