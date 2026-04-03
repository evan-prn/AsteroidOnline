using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AsteroidOnline.Client.Services;
using AsteroidOnline.Client.ViewModels;
using AsteroidOnline.Client.Views;
using AsteroidOnline.GameLogic.Interfaces;
using AsteroidOnline.Infrastructure.Networking;

using Application = Avalonia.Application;

namespace AsteroidOnline.Client;

/// <summary>
/// Point d'entrée de l'application AvaloniaUI.
/// Configure manuellement le conteneur de dépendances (DI léger sans framework externe)
/// et initialise la navigation vers l'écran de connexion au démarrage.
/// </summary>
public partial class App : Application
{
    // Services singleton partagés par tous les ViewModels.
    private LiteNetClientService?  _networkService;
    private NavigationService?     _navigationService;
    private MainWindowViewModel?   _mainWindowViewModel;

    // Timer global de polling réseau — actif en permanence pour que les paquets
    // soient traités quelle que soit la vue courante (ConnectView, LobbyView…).
    // GameViewModel appelle aussi PollEvents() dans sa propre boucle, ce double
    // appel est sans danger (LiteNetLib est idempotent sur PollEvents).
    private DispatcherTimer? _networkPollTimer;

    /// <inheritdoc/>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Suppression des doublons de validation CommunityToolkit + Avalonia.
            DisableAvaloniaDataAnnotationValidation();

            // ── Construction manuelle du conteneur DI ────────────────────────
            _networkService      = new LiteNetClientService();
            _mainWindowViewModel = new MainWindowViewModel();

            // Le NavigationService a besoin d'un accès au MainWindowViewModel
            // pour modifier CurrentViewModel ; on le branche via un callback.
            _navigationService = new NavigationService(
                viewModelFactory: CreateViewModel,
                setCurrentViewModel: vm =>
                    Dispatcher.UIThread.Post(() =>
                        _mainWindowViewModel.SetCurrentViewModel(vm)));

            // ── Fenêtre principale ───────────────────────────────────────────
            desktop.MainWindow = new MainWindow
            {
                DataContext = _mainWindowViewModel,
            };

            // ── Timer de polling réseau global (60 Hz) ───────────────────────
            // Garantit que PollEvents() est appelé à chaque frame quelle que
            // soit la vue affichée (ConnectView, LobbyView, GameView…).
            _networkPollTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(16),
                DispatcherPriority.Background,
                (_, _) => _networkService!.PollEvents());
            _networkPollTimer.Start();

            // ── Navigation initiale vers l'écran de connexion (US-01) ────────
            _navigationService.NavigateTo<ConnectViewModel>();

            // Arrêt propre du service réseau à la fermeture de l'application.
            desktop.Exit += (_, _) =>
            {
                _networkPollTimer.Stop();
                _networkService.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Factory de ViewModels : résout le type demandé et injecte les dépendances.
    /// Ajouter ici tout nouveau ViewModel introduit dans les blocs suivants.
    /// </summary>
    /// <param name="type">Type du ViewModel à instancier.</param>
    /// <returns>Nouvelle instance du ViewModel.</returns>
    /// <exception cref="InvalidOperationException">Type non enregistré.</exception>
    private object CreateViewModel(Type type)
    {
        if (type == typeof(ConnectViewModel))
            return new ConnectViewModel(_networkService!, _navigationService!);

        if (type == typeof(LobbyViewModel))
            return new LobbyViewModel(_networkService!, _navigationService!);

        if (type == typeof(GameViewModel))
            return new GameViewModel(_networkService!, _navigationService!);

        throw new InvalidOperationException(
            $"[App] ViewModel non enregistré dans la factory : {type.FullName}");
    }

    /// <summary>
    /// Désactive le plugin de validation de données d'Avalonia pour éviter
    /// les doublons avec les validations de CommunityToolkit.Mvvm.
    /// </summary>
    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var toRemove = BindingPlugins.DataValidators
            .OfType<DataAnnotationsValidationPlugin>()
            .ToArray();

        foreach (var plugin in toRemove)
            BindingPlugins.DataValidators.Remove(plugin);
    }
}
