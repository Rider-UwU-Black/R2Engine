using R2Engine.Runtime.Platform;

namespace R2Engine.Editor.Scene;

public class AudioSource : Component
{
    public string ClipPath = "";
    public bool PlayOnStart = true;
    public bool Loop;
    public bool Spatial;
    public float Volume = 1.0f;
    public float Pitch = 1.0f;
    public float MinDistance = 1.0f;
    public float MaxDistance = 25.0f;

    public void Play() => RuntimePlatform.Services.Audio?.AudioSystem?.Play(this);
    public void PlayOneShot() => RuntimePlatform.Services.Audio?.AudioSystem?.PlayOneShot(this);
    public void Pause() => RuntimePlatform.Services.Audio?.AudioSystem?.Pause(this);
    public void Stop() => RuntimePlatform.Services.Audio?.AudioSystem?.Stop(this);
}
