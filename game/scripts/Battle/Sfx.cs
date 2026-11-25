using Artillery.Core.Match;
using Artillery.Game.Audio;

namespace Artillery.Game.Battle;

/// <summary>Single call site for sound cues (AudioManager autoload).</summary>
public static class Sfx
{
    public static void Play(SoundId id) => AudioManager.Play(id);
}
