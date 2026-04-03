namespace AsteroidOnline.Domain.Entities;

/// <summary>
/// Couleurs disponibles pour les vaisseaux des joueurs.
/// Chaque valeur correspond à une teinte visuelle distincte sur la carte de jeu.
/// </summary>
public enum PlayerColor : byte
{
    /// <summary>Vaisseau rouge (#FF4444).</summary>
    Rouge = 0,

    /// <summary>Vaisseau bleu (#4488FF).</summary>
    Bleu = 1,

    /// <summary>Vaisseau vert (#44FF88).</summary>
    Vert = 2,

    /// <summary>Vaisseau jaune (#FFDD44).</summary>
    Jaune = 3,

    /// <summary>Vaisseau violet (#AA44FF).</summary>
    Violet = 4,

    /// <summary>Vaisseau orange (#FF8844).</summary>
    Orange = 5,
}
