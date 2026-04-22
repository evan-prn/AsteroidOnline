namespace AsteroidOnline.Client.Services;

using AsteroidOnline.Domain.Entities;

/// <summary>
/// Contexte de session du joueur local côté client.
/// Persiste à travers les changements de vues.
/// </summary>
public sealed class PlayerSession
{
    /// <summary>Identifiant serveur du joueur local.</summary>
    public int PlayerId { get; set; }

    /// <summary>Pseudo affiché.</summary>
    public string Pseudo { get; set; } = string.Empty;

    /// <summary>Couleur choisie dans l'écran de connexion.</summary>
    public PlayerColor Color { get; set; } = PlayerColor.Bleu;

    /// <summary>Réinitialise le contexte.</summary>
    public void Clear()
    {
        PlayerId = 0;
        Pseudo = string.Empty;
        Color = PlayerColor.Bleu;
    }
}
