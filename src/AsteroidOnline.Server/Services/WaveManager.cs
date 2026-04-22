namespace AsteroidOnline.Server.Services;

using AsteroidOnline.Domain.Entities;

/// <summary>
/// Gestionnaire de vagues d'astéroïdes (US-16).
/// Toutes les 30 secondes, déclenche une nouvelle vague qui augmente
/// le nombre d'astéroïdes de 20%, plafonné à 80.
/// </summary>
public sealed class WaveManager
{
    // Intervalle entre deux vagues en secondes.
    private const float WaveInterval = 30f;

    // Nombre maximum d'astéroïdes simultanés.
    public const int MaxAsteroids = 80;

    private float _waveTimer;

    /// <summary>Numéro de la vague courante (commence à 1).</summary>
    public int CurrentWave { get; private set; } = 1;

    /// <summary>
    /// Avance le timer de vague.
    /// Retourne <see langword="true"/> si une nouvelle vague doit être déclenchée.
    /// </summary>
    /// <param name="deltaTime">Durée du tick en secondes.</param>
    /// <param name="currentAsteroidCount">Nombre d'astéroïdes actuellement actifs.</param>
    public bool Tick(float deltaTime, int currentAsteroidCount)
    {
        // Pas de nouvelle vague si le plafond est atteint
        if (currentAsteroidCount >= MaxAsteroids)
            return false;

        _waveTimer += deltaTime;
        if (_waveTimer < WaveInterval)
            return false;

        _waveTimer -= WaveInterval;
        CurrentWave++;
        return true;
    }

    /// <summary>Secondes restantes avant la prochaine vague.</summary>
    public float SecondsUntilNextWave => MathF.Max(0f, WaveInterval - _waveTimer);

    /// <summary>Réinitialise l'état interne pour une nouvelle manche.</summary>
    public void Reset()
    {
        _waveTimer = 0f;
        CurrentWave = 1;
    }
}
