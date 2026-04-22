# Refactorisation globale — Revue technique

> **Date :** 2026-04-22
> **Branche :** `main`
> **Build :** ✅ 0 erreur · 2 avertissements (pré-existants — `Tmds.DBus.Protocol` vulnérabilité transitive Avalonia)
> **Fichiers analysés :** 64 fichiers .cs
> **Fichiers modifiés :** 4

---

## Résumé exécutif

Le projet est dans un état **remarquablement propre** à l'issue des BLOC 1–5. L'architecture Clean Architecture est respectée, les conventions C# sont globalement suivies, et les décisions documentées dans les reviews précédentes (BLOC 1–3, 5) sont cohérentes avec l'implémentation. Seules 4 corrections mineures ont été nécessaires.

---

## Modifications effectuées

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

*Document généré le 2026-04-22 — Refactorisation globale post-BLOC 5*
