using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AsteroidOnline.GameLogic.Interfaces;
using AsteroidOnline.Shared.Packets;

namespace AsteroidOnline.Client.ViewModels;

public sealed record FinalRankingEntry(int Rank, string PlayerName, int Score, bool IsAlive)
{
    public string StatusText => IsAlive ? "Survivant" : "Elimine";
}

/// <summary>
/// ViewModel de l'ecran de fin de partie.
/// </summary>
public partial class GameOverViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly INetworkClientService _networkService;
    private bool _returnInProgress;

    [ObservableProperty]
    private string _winnerName = string.Empty;

    [ObservableProperty]
    private bool _showWinner = true;

    [ObservableProperty]
    private int _finalScore;

    public IReadOnlyList<FinalRankingEntry> FinalRanking { get; }

    public GameOverViewModel(
        INavigationService navigationService,
        INetworkClientService networkService,
        string winnerName,
        int finalScore,
        bool isSoloMode,
        IReadOnlyList<FinalRankingEntry>? finalRanking = null)
    {
        _navigationService = navigationService;
        _networkService = networkService;
        ShowWinner = !isSoloMode;
        WinnerName = string.IsNullOrWhiteSpace(winnerName) ? "Aucun survivant" : winnerName;
        FinalScore = finalScore;
        FinalRanking = finalRanking ?? [];
    }

    [RelayCommand]
    private void ReturnToLobby()
    {
        if (_returnInProgress)
            return;

        _returnInProgress = true;
        _networkService.SendReliable(new ReturnToLobbyRequestPacket());
        _navigationService.NavigateTo<LobbyViewModel>();
    }
}
