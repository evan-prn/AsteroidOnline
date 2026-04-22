using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace AsteroidOnline.Client.Input;

/// <summary>
/// Gestionnaire des entrées clavier pour le vaisseau du joueur (US-07).
/// S'abonne aux événements <see cref="KeyDown"/> et <see cref="KeyUp"/> du <see cref="IInputElement"/>
/// de la fenêtre principale, maintient un ensemble de touches actives et expose
/// <see cref="GetCurrentState"/> pour lire l'état à chaque frame.
/// </summary>
/// <remarks>
/// Usage :
/// <code>
/// // Dans le code-behind de GameView (OnAttachedToVisualTree) :
/// _inputHandler = new InputHandler(TopLevel.GetTopLevel(this)!);
/// // Dans le game loop :
/// var state = _inputHandler.GetCurrentState();
/// </code>
/// </remarks>
public sealed class InputHandler : IDisposable
{
    private readonly IInputElement _inputSource;

    // Ensemble des touches actuellement maintenues enfoncées.
    private readonly HashSet<Key> _pressedKeys = new();

    // Verrou pour protéger _pressedKeys contre les accès concurrents
    // (événements UI thread vs lecture depuis le timer de jeu).
    private readonly object _lock = new();

    /// <summary>
    /// Initialise le gestionnaire d'inputs et s'abonne aux événements clavier.
    /// </summary>
    /// <param name="inputSource">
    ///   Source des événements clavier (généralement le <c>TopLevel</c> / Window).
    /// </param>
    public InputHandler(IInputElement inputSource)
    {
        _inputSource = inputSource;
        _inputSource.KeyDown += OnKeyDown;
        _inputSource.KeyUp   += OnKeyUp;
    }

    /// <summary>
    /// Retourne un snapshot de l'état courant des touches de jeu.
    /// Thread-safe : peut être appelé depuis le timer de jeu.
    /// </summary>
    public PlayerInputState GetCurrentState()
    {
        lock (_lock)
        {
            return new PlayerInputState
            {
                // Supporte WASD + ZQSD + fleches.
                ThrustForward = _pressedKeys.Contains(Key.W)
                             || _pressedKeys.Contains(Key.Z)
                             || _pressedKeys.Contains(Key.Up),
                RotateLeft    = _pressedKeys.Contains(Key.A)
                             || _pressedKeys.Contains(Key.Q)
                             || _pressedKeys.Contains(Key.Left),
                RotateRight   = _pressedKeys.Contains(Key.D)
                             || _pressedKeys.Contains(Key.Right),
                Fire          = _pressedKeys.Contains(Key.Space) || _pressedKeys.Contains(Key.F),
                Dash          = _pressedKeys.Contains(Key.LeftShift)
                             || _pressedKeys.Contains(Key.RightShift)
                             || _pressedKeys.Contains(Key.E),
            };
        }
    }

    /// <summary>Réinitialise toutes les touches (utile lors d'une perte de focus).</summary>
    public void ClearAll()
    {
        lock (_lock)
        {
            _pressedKeys.Clear();
        }
    }

    // ──── Handlers ────────────────────────────────────────────────────────────

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        lock (_lock)
        {
            _pressedKeys.Add(e.Key);
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        lock (_lock)
        {
            _pressedKeys.Remove(e.Key);
        }
    }

    /// <summary>Se désabonne des événements clavier à la destruction.</summary>
    public void Dispose()
    {
        _inputSource.KeyDown -= OnKeyDown;
        _inputSource.KeyUp   -= OnKeyUp;
        ClearAll();
    }
}
