namespace AsteroidOnline.GameLogic.Interfaces;

/// <summary>
/// Service de navigation entre les ViewModels de l'application cliente.
/// Permet de changer la vue affichée depuis n'importe quel ViewModel
/// sans créer de couplage direct entre eux.
/// L'implémentation concrète réside dans le projet Client.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Crée une nouvelle instance du ViewModel spécifié et navigue vers elle.
    /// La résolution de l'instance est déléguée à la factory enregistrée.
    /// </summary>
    /// <typeparam name="TViewModel">Type du ViewModel cible.</typeparam>
    void NavigateTo<TViewModel>() where TViewModel : class;

    /// <summary>
    /// Navigue directement vers une instance existante de ViewModel.
    /// Utile lorsque le ViewModel a déjà été construit avec un état particulier.
    /// </summary>
    /// <param name="viewModel">L'instance vers laquelle naviguer.</param>
    void NavigateTo(object viewModel);
}
