using R2Engine.Editor.Scene;

namespace R2Engine.Runtime.Audio;

/// <summary>Platform-neutral audio playback surface used by runtime components.</summary>
public interface IRuntimeAudioSystem : IDisposable
{
    void Update(Scene scene);
    void Play(AudioSource source);
    void PlayOneShot(AudioSource source);
    void PlayUiSound(string clipPath, bool splitVariants = true);
    void Pause(AudioSource source);
    void Stop(AudioSource source);
    void Release(AudioSource source);
}
