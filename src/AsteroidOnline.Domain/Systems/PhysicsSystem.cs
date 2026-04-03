namespace AsteroidOnline.Domain.Systems;

using System.Numerics;
using AsteroidOnline.Domain.Entities;
using AsteroidOnline.Domain.World;

/// <summary>
/// Système de physique appliquant l'inertie et le wrap-around toroïdal
/// à toutes les entités physiques du monde de jeu (US-08, US-12).
/// Ce système est <b>sans état</b> : toutes les données résident dans les entités.
/// Il est exécuté à 60 Hz sur le serveur autoritaire (US-27).
/// </summary>
public class PhysicsSystem
{
    // Coefficient de frottement spatial appliqué à chaque tick.
    // Légèrement inférieur à 1 pour simuler l'inertie (flottement spatial).
    private const float DragCoefficient = 0.99f;

    /// <summary>
    /// Applique un tick de physique à une entité quelconque (astéroïde, projectile).
    /// Déplace l'entité selon sa vélocité et applique l'amortissement et l'AngularVelocity.
    /// </summary>
    /// <param name="entity">Entité à mettre à jour.</param>
    /// <param name="deltaTime">Durée du tick en secondes (≈ 1/60).</param>
    /// <param name="bounds">Dimensions du monde pour le wrap-around toroïdal.</param>
    public void Tick(PhysicalEntity entity, float deltaTime, in WorldBounds bounds)
    {
        // Rotation angulaire (astéroïdes en rotation libre)
        entity.Rotation += entity.AngularVelocity * deltaTime;

        // Déplacement selon la vélocité courante
        entity.Position += entity.Velocity * deltaTime;

        // Amortissement
        entity.Velocity *= DragCoefficient;

        // Wrap-around toroïdal (US-12)
        ApplyWrapAround(entity, in bounds);
    }

    /// <summary>
    /// Applique un tick de physique à un vaisseau contrôlé par un joueur.
    /// Prend en compte les inputs de poussée et de rotation avant de déplacer l'entité.
    /// </summary>
    /// <param name="ship">Vaisseau à mettre à jour.</param>
    /// <param name="thrustForward">
    ///   <see langword="true"/> si la touche de poussée avant est maintenue.
    /// </param>
    /// <param name="rotateLeft">
    ///   <see langword="true"/> si la touche de rotation gauche est maintenue.
    /// </param>
    /// <param name="rotateRight">
    ///   <see langword="true"/> si la touche de rotation droite est maintenue.
    /// </param>
    /// <param name="deltaTime">Durée du tick en secondes.</param>
    /// <param name="bounds">Dimensions du monde pour le wrap-around toroïdal.</param>
    public void Tick(Ship ship, bool thrustForward, bool rotateLeft, bool rotateRight,
        float deltaTime, in WorldBounds bounds)
    {
        // ── Rotation ──────────────────────────────────────────────────────────
        if (rotateLeft)  ship.Rotation -= ship.RotationSpeed * deltaTime;
        if (rotateRight) ship.Rotation += ship.RotationSpeed * deltaTime;

        // ── Poussée (thrust) ──────────────────────────────────────────────────
        if (thrustForward)
        {
            // Direction de poussée dérivée de la rotation (repère trigonométrique).
            // Rotation = 0 → pointe vers le haut → sin(0)=0, -cos(0)=-1 → (0,-1) = haut
            var thrustDir = new Vector2(
                MathF.Sin(ship.Rotation),
               -MathF.Cos(ship.Rotation));

            ship.Velocity += thrustDir * (ship.ThrustForce * deltaTime);

            // Plafonnement de la vitesse (US-08 : MaxSpeed = 400 u/s)
            var speed = ship.Velocity.Length();
            if (speed > ship.MaxSpeed)
                ship.Velocity = ship.Velocity / speed * ship.MaxSpeed;
        }

        // ── Amortissement ─────────────────────────────────────────────────────
        ship.Velocity *= DragCoefficient;

        // ── Déplacement ───────────────────────────────────────────────────────
        ship.Position += ship.Velocity * deltaTime;

        // ── Wrap-around toroïdal (US-12) ─────────────────────────────────────
        ApplyWrapAround(ship, in bounds);
    }

    /// <summary>
    /// Applique le wrap-around toroïdal : si une entité sort d'un bord,
    /// elle réapparaît du bord opposé.
    /// </summary>
    private static void ApplyWrapAround(PhysicalEntity entity, in WorldBounds bounds)
    {
        var pos = entity.Position;

        // Axe X : sortie à droite → réapparaît à gauche, et vice-versa
        if (pos.X < 0f)          pos.X += bounds.Width;
        else if (pos.X >= bounds.Width)  pos.X -= bounds.Width;

        // Axe Y : sortie en bas → réapparaît en haut, et vice-versa
        if (pos.Y < 0f)           pos.Y += bounds.Height;
        else if (pos.Y >= bounds.Height) pos.Y -= bounds.Height;

        entity.Position = pos;
    }
}
