namespace AsteroidOnline.Client.Services;

/// <summary>
/// Service audio léger pour les feedbacks gameplay côté client.
/// </summary>
public interface IGameAudioService
{
    /// <summary>Joue un son de tir.</summary>
    void PlayShot();

    /// <summary>Joue un son d'explosion d'astéroïde.</summary>
    void PlayAsteroidExplosion();
}
