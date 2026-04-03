using CommunityToolkit.Mvvm.ComponentModel;

namespace AsteroidOnline.Client.ViewModels;

/// <summary>
/// ViewModel de la fenêtre principale.
/// Contient le ViewModel courant affiché dans la zone de contenu de <c>MainWindow</c>.
/// Le <see cref="ViewLocator"/> résout automatiquement la vue correspondante
/// en remplaçant "ViewModel" par "View" dans le nom de type complet.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    /// ViewModel actuellement affiché dans le ContentControl central.
    /// Changer cette propriété remplace la vue visible à l'écran.
    /// </summary>
    [ObservableProperty]
    private object? _currentViewModel;

    /// <summary>
    /// Remplace le ViewModel courant depuis le thread UI.
    /// Appelé par <see cref="Services.NavigationService"/> via un callback.
    /// </summary>
    /// <param name="viewModel">Nouvelle instance de ViewModel à afficher.</param>
    public void SetCurrentViewModel(object viewModel)
    {
        CurrentViewModel = viewModel;
    }
}
