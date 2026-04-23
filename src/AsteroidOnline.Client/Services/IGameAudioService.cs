namespace AsteroidOnline.Client.Services;

/// <summary>
/// Service audio leger pour les feedbacks gameplay et la musique d'ambiance.
/// </summary>
public interface IGameAudioService
{
    /// <summary>Joue un son de tir.</summary>
    void PlayShot();

    /// <summary>Joue un son d'explosion d'asteroide.</summary>
    void PlayAsteroidExplosion();

    /// <summary>Demarre la musique d'ambiance en boucle si disponible.</summary>
    void StartAmbientLoop();

    /// <summary>Arrete la musique d'ambiance.</summary>
    void StopAmbientLoop();
}
