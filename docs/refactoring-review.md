# Refactorisation globale — Revue technique

> **Date initiale :** 2026-04-22 | **Mise à jour :** 2026-04-23
> **Branche :** `main`
> **Build :** ✅ 0 erreur · 2 avertissements (pré-existants — `Tmds.DBus.Protocol` vulnérabilité transitive Avalonia)
> **Fichiers analysés (cumul) :** 42 fichiers sources (.cs)
> **Fichiers modifiés (cycle 2026-04-23) :** 6

---

## Résumé exécutif (mise à jour 2026-04-23)

Le projet restait dans un état très propre après le premier cycle de refactorisation (2026-04-22). Un second passage complet sur les 42 fichiers sources a identifié **6 corrections** supplémentaires : 1 bug (score non réinitialisé), 1 bug latent (vitesse de projectile hardcodée), 1 code mort, 1 duplication, 1 fragilité de notification, et 3 modificateurs `sealed` manquants.

---

## Modifications cycle 2026-04-23

### 5. `Domain/Systems/PhysicsSystem.cs`

**Problème :** Classe non `sealed` alors que tous les autres systèmes sans état (`CollisionSystem`, `WaveManager`) le sont — incohérence de convention.

**Correction :** Ajout de `sealed`.

---

### 6. `Domain/Systems/WeaponSystem.cs`

**Problème 1 (bug latent) :** `Velocity = direction * 700f + ship.Velocity` hardcode la vitesse du projectile. Or `Projectile.Speed` existe pour représenter cette valeur. Si un power-up futur (BLOC 4) devait modifier `Projectile.Speed`, la vélocité réelle resterait figée à 700 u/s — `Speed` aurait été du code mort.

```csharp
// Avant
Velocity = direction * 700f + ship.Velocity,

// Après
var projectile = new Projectile { Id = ..., OwnerId = ..., Position = ..., Direction = direction };
projectile.Velocity = direction * projectile.Speed + ship.Velocity;
return projectile;
```

**Problème 2 :** Classe non `sealed` (même raison que `PhysicsSystem`).

**Correction :** Utilisation de `projectile.Speed` pour le calcul de vélocité + ajout de `sealed`.

---

### 7. `Domain/Systems/DashSystem.cs`

**Problème :** Classe non `sealed` — même raison que les deux précédents.

**Correction :** Ajout de `sealed`. Les `public const` (`DashDuration`, `CooldownDuration`) restent accessibles via `DashSystem.CooldownDuration` sans changement.

---

### 8. `Server/GameLoop.cs`

**Problème (bug) :** `Ship.Score` n'est jamais réinitialisé entre les parties. Dans `StartGame()`, l'état des vaisseaux est remis à zéro (position, vélocité, dash, arme, IsAlive) — mais pas le score. Résultat : un joueur jouant deux parties de suite commence la deuxième avec le score cumulatif de la première.

```csharp
// Ajouté dans StartGame(), dans la boucle foreach (var ship in _ships.Values)
ship.Score = 0;
```

**Décision :** Réinitialisation dans `StartGame()` uniquement (pas dans `ResetMatchToLobby()`), pour préserver la possibilité d'afficher un classement inter-parties dans le lobby à l'avenir.

---

### 9. `Client/Rendering/GameRenderer.cs`

**Problème 1 (code mort) :** `ShieldBrush` défini mais jamais utilisé. Il n'est référencé nulle part dans le fichier. Son existence suggère faussement que les boucliers sont déjà rendus.

**Correction :** Suppression du champ `ShieldBrush`.

**Problème 2 (duplication) :** `WorldWidth = 1920f` et `WorldHeight = 1080f` dupliquent exactement `WorldBounds.Default`. Si les dimensions du monde changent dans `WorldBounds`, le renderer continuerait à scaler sur les anciennes valeurs.

**Correction :** Remplacement des constantes locales par `WorldBounds.Default.Width/.Height`. Ajout de l'`using AsteroidOnline.Domain.World;`.

---

### 10. `Client/ViewModels/LobbyViewModel.cs`

**Problème (fragilité) :** `CanStartSolo` est notifié via `OnPropertyChanged(nameof(CanStartSolo))` appelé manuellement dans 4 endroits. Si une nouvelle propriété dépendante est ajoutée à `CanStartSolo`, il faut penser à ajouter la notification dans tous les handlers. CommunityToolkit fournit `[NotifyPropertyChangedFor]` pour automatiser ces notifications depuis les setters générés.

**Correction :** Ajout de `[NotifyPropertyChangedFor(nameof(CanStartSolo))]` sur `_playerCount`, `_isCountingDown`, et `_hostPlayerId`. Les `OnPropertyChanged(nameof(CanStartSolo))` existants dans les handlers sont conservés (redondants mais inoffensifs).

---

## Modifications cycle 2026-04-22

### 1. `Domain/Systems/CollisionSystem.cs`

**Problème :** Directive `using AsteroidOnline.Domain.Events;` inutilisée — aucun type de ce namespace n'est utilisé dans le fichier.

**Cause probable :** Résidu d'une version antérieure où la logique de fragmentation était gérée dans `CollisionSystem` avant d'être déplacée dans `AsteroidSpawnService`.

**Correction :** Suppression de la directive.

---

### 2. `Client/ViewModels/GameViewModel.cs`

**Problème 1 (bug utilisateur visible) :** Ligne 196 — `"a elimine"` dans le feed d'éliminations HUD.

```csharp
// Avant
EliminationFeedText = $"{packet.KillerName} a elimine {packet.VictimName}";

// Après
EliminationFeedText = $"{packet.KillerName} a éliminé {packet.VictimName}";
```

Ce texte est affiché à l'écran pendant 4 secondes à chaque kill. L'accent manquant était visible par tous les joueurs.

**Problème 2 (commentaires) :** La doc XML de la classe avait 4 mots sans accents (`ecran`, `Gere`, `a 60 Hz`, `reception`), incohérent avec le reste du projet.

**Correction :** Ajout des accents manquants dans le commentaire de classe.

---

### 3. `Client/ViewModels/GameOverViewModel.cs`

**Problème :** Commentaire XML : `"l'ecran"` → `"l'écran"`.

**Correction :** Ajout de l'accent manquant.

---

### 4. `Client/App.axaml.cs`

**Problème :** Entrée factory pour `GameOverViewModel` — code mort et trompeur.

```csharp
// Supprimé
if (type == typeof(GameOverViewModel))
    return new GameOverViewModel(_navigationService!, _networkService!, "Personne", 0, isSoloMode: false);
```

`GameOverViewModel` n'est **jamais** instancié via `NavigateTo<GameOverViewModel>()`. Il est toujours créé directement dans `GameViewModel.HandleGameOver()` via `NavigateTo(new GameOverViewModel(...))`. Cette branche factory retournait de fausses données (`"Personne"`, score `0`) et ne pouvait jamais être déclenchée.

**Correction :** Suppression de la branche morte.

---

## Architecture — État post-refactorisation

### Graphe de dépendances (inchangé)

```
Domain (aucune dépendance externe)
    ▲
Shared (→ Domain)
    ▲           ▲
GameLogic    Server (→ Domain, Shared, LiteNetLib, Microsoft.Extensions.Logging)
    ▲
Infrastructure (→ GameLogic, Shared, LiteNetLib)
    ▲
Client (→ Infrastructure, Shared, Avalonia, CommunityToolkit.Mvvm)
```

### Navigation des ViewModels

```
App (composition root)
    └─ ConnectViewModel ──[LobbyJoined]──► LobbyViewModel ──[Countdown=0]──► GameViewModel
                                                                                    │
                                                           [GameOver]──► GameOverViewModel
                                                                                    │
                                                           [ReturnToLobby]──► LobbyViewModel
```

---

## Décisions techniques confirmées (cycle 2026-04-23)

| Décision | Justification |
|----------|---------------|
| `Score` réinitialisé dans `StartGame()` uniquement | Préserve un futur classement inter-parties dans le lobby |
| `ShieldBrush` supprimé (pas déplacé) | Le rendu de bouclier sera ajouté avec une méthode `DrawShield()` dans BLOC 4 |
| `AsteroidPen` et `ShipPen` conservés | Peuvent être utilisés pour les contours dans une amélioration visuelle future |
| `O(n)` lookup dans `GameLoop` non corrigé | Documenté, non bloquant pour ≤ 16 joueurs (voir ci-dessous) |

---

## Points restant à améliorer (non bloquants)

### Performance — Lookup O(n) dans GameLoop

`GameLoop.cs` effectue plusieurs fois `_peers.FirstOrDefault(kv => kv.Value == peer)` pour retrouver l'ID joueur à partir d'un `NetPeer`. Pour 16 joueurs, c'est négligeable (coût < 1 µs). Si le nombre de joueurs augmente, ajouter un dictionnaire inversé `Dictionary<NetPeer, int>` ramènerait la complexité à O(1).

### Vulnérabilité `Tmds.DBus.Protocol`

Dépendance transitive d'Avalonia. À mettre à jour lorsqu'Avalonia publie une version corrigeant cette dépendance. Non modifiable directement dans ce projet.

### `GameOverView.axaml.cs` — Classe sans doc XML

`GameOverView` est la seule vue sans commentaire de classe. Mineur — cohérence avec les autres vues.

### Encodage fichiers

Les fichiers `GameViewModel.cs` et `GameOverViewModel.cs` avaient des accents manquants. Probable édition dans un environnement avec un encodage différent (ex. terminal sans UTF-8). S'assurer que l'éditeur est configuré UTF-8 sans BOM pour éviter ce type de régression.

---

## Décisions techniques confirmées (non remises en cause)

| Décision | Justification |
|----------|---------------|
| DI manuel via `CreateViewModel` factory | Suffisant pour 3 ViewModels actifs, pas besoin de `Microsoft.Extensions.DependencyInjection` |
| `GameOverViewModel` instancié directement (pas via factory) | Nécessaire pour passer le vrai `winnerName` et `score` |
| Sérialisation binaire (`BinaryWriter/Reader`) | Performances UDP ; compromis documenté |
| `ConcurrentDictionary` pour les inputs (dernier input par joueur) | Thread-safe, évite la file qui accumulerait du retard |
| Vaisseaux morts conservés dans `_ships` | Snapshots avec `IsAlive=false` pour animation côté client |
| `LobbyPlayerInfo.ColorHex` dans Shared | Évite un wrapper VM côté Client pour 6 valeurs stables |

---

*Document généré le 2026-04-22, mis à jour le 2026-04-23 — Refactorisation globale post-BLOC 5*
