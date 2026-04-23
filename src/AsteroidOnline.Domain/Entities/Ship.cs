namespace AsteroidOnline.Domain.Entities;

/// <summary>
/// Vaisseau d'un joueur. Étend <see cref="PhysicalEntity"/> avec les paramètres
/// de pilotage, l'état du dash et l'état de l'arme.
/// Le serveur est autoritaire sur toutes ces valeurs (US-27).
/// </summary>
public class Ship : PhysicalEntity
{
    // ──── Identification ──────────────────────────────────────────────────────

    /// <summary>Pseudo affiché au-dessus du vaisseau.</summary>
    public string Pseudo { get; set; } = string.Empty;

    /// <summary>Couleur de vaisseau choisie dans le lobby (US-05).</summary>
    public PlayerColor Color { get; set; } = PlayerColor.Bleu;

    /// <summary>Indique si le joueur est encore en vie.</summary>
    public bool IsAlive { get; set; } = true;

    /// <summary>
    /// Score total du joueur (kills + astéroïdes détruits).
    /// </summary>
    public int Score { get; set; }

    /// <summary>Nombre de vies restantes (3 par défaut).</summary>
    public int LivesRemaining { get; set; } = 3;

    /// <summary>
    /// Temps d'invulnérabilité restant en secondes.
    /// </summary>
    public float InvulnerabilityRemaining { get; set; }

    /// <summary>Indique si le joueur est invulnérable.</summary>
    public bool IsInvulnerable => InvulnerabilityRemaining > 0f;

    // ──── Paramètres de mouvement (US-08) ────────────────────────────────────

    /// <summary>
    /// Force de poussée en unités/s². Appliquée à la vélocité quand le thrust est actif.
    /// Valeur par défaut : 300 u/s².
    /// </summary>
    public float ThrustForce { get; set; } = 300f;

    /// <summary>
    /// Vitesse de rotation en radians/seconde lors d'un appui RotateLeft/RotateRight.
    /// Valeur par défaut : 3 rad/s (~172°/s).
    /// </summary>
    public float RotationSpeed { get; set; } = 3f;

    /// <summary>
    /// Plafond de vitesse en unités/seconde (US-08).
    /// La vélocité est clampée à cette valeur après chaque tick.
    /// Valeur par défaut : 400 u/s.
    /// </summary>
    public float MaxSpeed { get; set; } = 400f;

    // ──── État du dash (US-11) ────────────────────────────────────────────────

    /// <summary>Indique si l'impulsion de dash est actuellement active.</summary>
    public bool IsDashing { get; set; }

    /// <summary>Durée restante de l'impulsion de dash en secondes (max 0.3s).</summary>
    public float DashTimeRemaining { get; set; }

    /// <summary>
    /// Temps restant avant que le dash soit à nouveau disponible (max 3s).
    /// 0 = dash disponible immédiatement.
    /// </summary>
    public float DashCooldown { get; set; }

    // ──── État de l'arme (US-09) ──────────────────────────────────────────────

    /// <summary>
    /// Temps restant avant que l'arme puisse tirer à nouveau.
    /// 0 = tir disponible. Normal : 0.25s ; RapidFire (US-20) : 0.08s.
    /// </summary>
    public float WeaponCooldown { get; set; }

    // ──── PhysicalEntity ──────────────────────────────────────────────────────

    /// <summary>Rayon de collision du vaisseau : 16 unités.</summary>
    public override float CollisionRadius => 16f;
}
