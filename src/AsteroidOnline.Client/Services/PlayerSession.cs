namespace AsteroidOnline.Client.Services;

using System.Collections.Generic;
using AsteroidOnline.Domain.Entities;
using AsteroidOnline.Shared.Packets;

/// <summary>
/// Contexte de session du joueur local côté client.
/// Persiste à travers les changements de vues.
/// </summary>
public sealed class PlayerSession
{
    private readonly Dictionary<int, string> _playerNames = new();

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
        _playerNames.Clear();
    }

    /// <summary>Met Ã  jour la table locale des pseudos connus.</summary>
    public void UpdateRoster(IEnumerable<LobbyPlayerInfo> players)
    {
        _playerNames.Clear();
        foreach (var player in players)
        {
            _playerNames[player.Id] = string.IsNullOrWhiteSpace(player.Pseudo)
                ? $"Joueur{player.Id}"
                : player.Pseudo;
        }
    }

    /// <summary>Retourne le pseudo associÃ© Ã  un identifiant de joueur.</summary>
    public string GetPlayerName(int playerId)
    {
        if (playerId == PlayerId && !string.IsNullOrWhiteSpace(Pseudo))
            return Pseudo;

        return _playerNames.TryGetValue(playerId, out var name)
            ? name
            : $"Joueur{playerId}";
    }

    /// <summary>Snapshot immutable de la table des pseudos pour le renderer.</summary>
    public IReadOnlyDictionary<int, string> GetRosterSnapshot()
        => new Dictionary<int, string>(_playerNames);

    /// <summary>Assure que le joueur local existe dans le roster.</summary>
    public void EnsureLocalPlayerRegistered()
    {
        if (PlayerId <= 0)
            return;

        _playerNames[PlayerId] = string.IsNullOrWhiteSpace(Pseudo)
            ? $"Joueur{PlayerId}"
            : Pseudo;
    }
}
