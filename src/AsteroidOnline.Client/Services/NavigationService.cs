using System;
using AsteroidOnline.GameLogic.Interfaces;

namespace AsteroidOnline.Client.Services;

/// <summary>
/// Implémentation du service de navigation pour le client AvaloniaUI.
/// Délègue la création des ViewModels à une factory injectée,
/// et notifie la <see cref="ViewModels.MainWindowViewModel"/> via un callback
/// pour qu'elle remplace son ViewModel courant.
/// </summary>
public sealed class NavigationService : INavigationService
{
    // Factory qui instancie un ViewModel par son type (branchée sur le conteneur DI).
    private readonly Func<Type, object> _viewModelFactory;

    // Callback appelé sur le thread UI pour mettre à jour la vue courante.
    private readonly Action<object> _setCurrentViewModel;

    /// <summary>
    /// Initialise le service de navigation.
    /// </summary>
    /// <param name="viewModelFactory">
    ///   Fonction retournant une instance du type demandé (ex: résolution DI).
    /// </param>
    /// <param name="setCurrentViewModel">
    ///   Action qui met à jour le ViewModel courant de la fenêtre principale.
    ///   Doit être thread-safe vis-à-vis du thread UI (utiliser Dispatcher.UIThread si nécessaire).
    /// </param>
    public NavigationService(
        Func<Type, object> viewModelFactory,
        Action<object> setCurrentViewModel)
    {
        _viewModelFactory      = viewModelFactory;
        _setCurrentViewModel   = setCurrentViewModel;
    }

    /// <inheritdoc/>
    public void NavigateTo<TViewModel>() where TViewModel : class
    {
        var vm = _viewModelFactory(typeof(TViewModel));
        _setCurrentViewModel(vm);
    }

    /// <inheritdoc/>
    public void NavigateTo(object viewModel)
    {
        _setCurrentViewModel(viewModel);
    }
}
