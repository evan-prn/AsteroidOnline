namespace AsteroidOnline.Domain.Systems;

using AsteroidOnline.Domain.Entities;

/// <summary>
/// Système de gestion du dash (boost temporaire de vitesse, US-11).
/// Le dash applique une impulsion ×2.5 pendant 0.3 secondes,
/// suivie d'un cooldown de 3 secondes avant réutilisation.
/// Exécuté côté serveur autoritaire (US-27).
/// </summary>
public sealed class DashSystem
{
    /// <summary>Durée de l'impulsion de dash en secondes.</summary>
    public const float DashDuration = 0.3f;

    /// <summary>Durée du cooldown après un dash en secondes.</summary>
    public const float CooldownDuration = 3f;

    /// <summary>Multiplicateur de vitesse appliqué lors du dash.</summary>
    private const float DashMultiplier = 2.5f;

    /// <summary>
    /// Traite le dash pour un vaisseau donné.
    /// À appeler à chaque tick du serveur, <b>avant</b> <see cref="PhysicsSystem.Tick"/>.
    /// </summary>
    /// <param name="ship">Vaisseau à mettre à jour.</param>
    /// <param name="dashInput">
    ///   <see langword="true"/> si la touche de dash vient d'être pressée ce tick.
    /// </param>
    /// <param name="deltaTime">Durée du tick en secondes.</param>
    /// <returns>
    ///   <see langword="true"/> si un dash vient d'être déclenché ce tick
    ///   (utile pour déclencher un effet sonore ou visuel).
    /// </returns>
    public bool Tick(Ship ship, bool dashInput, float deltaTime)
    {
        var dashTriggered = false;

        // ── Déclenchement du dash ─────────────────────────────────────────────
        if (dashInput && !ship.IsDashing && ship.DashCooldown <= 0f && ship.IsAlive)
        {
            // Impulsion immédiate : la vélocité est multipliée par DashMultiplier
            ship.Velocity    *= DashMultiplier;
            ship.IsDashing    = true;
            ship.DashTimeRemaining = DashDuration;
            dashTriggered     = true;
        }

        // ── Expiration de l'impulsion active ─────────────────────────────────
        if (ship.IsDashing)
        {
            ship.DashTimeRemaining -= deltaTime;
            if (ship.DashTimeRemaining <= 0f)
            {
                ship.IsDashing        = false;
                ship.DashTimeRemaining = 0f;
                // Démarrage du cooldown
                ship.DashCooldown     = CooldownDuration;
            }
        }

        // ── Décompte du cooldown ──────────────────────────────────────────────
        if (ship.DashCooldown > 0f)
            ship.DashCooldown = MathF.Max(0f, ship.DashCooldown - deltaTime);

        return dashTriggered;
    }

    /// <summary>
    /// Calcule la progression de la recharge (0.0 = cooldown plein, 1.0 = prêt).
    /// Utilisé pour l'affichage de la barre de recharge dans le HUD (US-11).
    /// </summary>
    /// <param name="ship">Vaisseau dont on veut lire la progression.</param>
    public static float GetCooldownProgress(Ship ship)
    {
        if (ship.IsDashing)     return 0f;
        if (ship.DashCooldown <= 0f) return 1f;
        return 1f - ship.DashCooldown / CooldownDuration;
    }
}
