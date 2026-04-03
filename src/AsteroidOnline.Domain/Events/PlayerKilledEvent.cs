namespace AsteroidOnline.Domain.Events;

/// <summary>
/// Événement déclenché quand un joueur est éliminé (US-15, US-22).
/// Diffusé à tous les clients via TCP après traitement serveur.
/// </summary>
public sealed class PlayerKilledEvent
{
    /// <summary>Identifiant du joueur éliminé.</summary>
    public int VictimId { get; init; }

    /// <summary>Pseudo du joueur éliminé (pour l'affichage dans le feed).</summary>
    public string VictimName { get; init; } = string.Empty;

    /// <summary>
    /// Identifiant du joueur ayant tué la victime.
    /// -1 si tué par un astéroïde.
    /// </summary>
    public int KillerId { get; init; } = -1;

    /// <summary>Pseudo du tueur. "Astéroïde" si tué par un astéroïde.</summary>
    public string KillerName { get; init; } = "Astéroïde";
}
