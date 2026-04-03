# BLOC 2 — Pilotage & Physique : Revue technique détaillée

> **Date :** 2026-04-03
> **Branche :** `develop`
> **Build :** ✅ 0 erreur · 0 avertissement
> **User Stories couvertes :** US-07, US-08, US-09, US-10, US-11, US-12

---

## Table des matières

1. [Vue d'ensemble des changements](#1-vue-densemble-des-changements)
2. [Domain — Entités physiques](#2-domain--entités-physiques)
3. [Domain — Systèmes](#3-domain--systèmes)
4. [Shared — Paquet d'input](#4-shared--paquet-dinput)
5. [Server — SpawnService](#5-server--spawnservice)
6. [Client — Gestion des inputs](#6-client--gestion-des-inputs)
7. [Client — GameViewModel](#7-client--gameviewmodel)
8. [Client — GameView AXAML](#8-client--gameview-axaml)
9. [Client — Convertisseurs HUD](#9-client--convertisseurs-hud)
10. [Mises à jour des fichiers existants](#10-mises-à-jour-des-fichiers-existants)
11. [Architecture & flux de données](#11-architecture--flux-de-données)
12. [Décisions techniques & compromis](#12-décisions-techniques--compromis)
13. [Points d'attention pour la suite](#13-points-dattention-pour-la-suite)

---

## 1. Vue d'ensemble des changements

Le BLOC 2 introduit toute la couche **physique et pilotage** du jeu : entités cinématiques,
systèmes de physique, capture des inputs clavier, et le premier écran de jeu fonctionnel.

**Statistiques :**

| Catégorie | Nombre |
|-----------|--------|
| Fichiers créés | 15 |
| Fichiers modifiés | 2 |
| Lignes de code ajoutées (estimation) | ~700 |
| Erreurs de build | 0 |

---

## 2. Domain — Entités physiques

```
Domain/
├── Entities/
│   ├── PhysicalEntity.cs   ← NOUVEAU (classe de base)
│   ├── Ship.cs             ← NOUVEAU
│   └── Projectile.cs       ← NOUVEAU
└── World/
    └── WorldBounds.cs      ← NOUVEAU
```

### 2.1 `PhysicalEntity.cs` — Base cinématique (US-08)

Classe abstraite commune à tous les objets soumis à la physique.

| Propriété | Type | Rôle |
|-----------|------|------|
| `Id` | `int` | Identifiant serveur unique |
| `Position` | `Vector2` | Position dans l'espace de jeu |
| `Velocity` | `Vector2` | Vélocité en u/s |
| `Rotation` | `float` | Angle en radians (0 = haut) |
| `AngularVelocity` | `float` | Rotation libre (astéroïdes) |
| `CollisionRadius` | `float` (abstract) | Rayon cercle de collision |

**Choix de `System.Numerics.Vector2`** (conforme au prompt maître) : type SIMD-friendly natif .NET, aucune allocation, accès aux méthodes `Length()`, `Distance()`, `Normalize()` sans code custom.

---

### 2.2 `Ship.cs` — Vaisseau joueur (US-08, US-09, US-11)

Étend `PhysicalEntity` avec trois groupes de propriétés :

**Identité :**
- `Pseudo`, `Color`, `IsAlive`

**Paramètres de mouvement (US-08) :**

| Propriété | Valeur par défaut | Description |
|-----------|-------------------|-------------|
| `ThrustForce` | 300 u/s² | Accélération à chaque tick de poussée |
| `RotationSpeed` | 3 rad/s | ~172°/s, rotation fluide |
| `MaxSpeed` | 400 u/s | Plafond de vélocité |

**État du dash (US-11) :**
- `IsDashing`, `DashTimeRemaining` (0.3s), `DashCooldown` (3s)

**État de l'arme (US-09) :**
- `WeaponCooldown` : 0.25s normal, 0.08s RapidFire (BLOC 4)

`CollisionRadius` = **16 unités** (rayon visuel approximatif du triangle vaisseau).

---

### 2.3 `Projectile.cs` — Projectile (US-09)

| Propriété | Valeur | Description |
|-----------|--------|-------------|
| `Speed` | 700 u/s | Plus rapide que le vaisseau max (400 u/s) |
| `LifetimeRemaining` | 2s | Durée de vie avant expiration |
| `IsActive` | bool | Mis à `false` à l'impact ou expiration |
| `OwnerId` | int | Évite l'auto-collision (US-22) |
| `CollisionRadius` | 4 u | Petit pour rester fair |

**Vélocité composite :** `direction × 700 + ship.Velocity`. Le projectile hérite de la vitesse du vaisseau, ce qui donne un comportement physiquement réaliste (un vaisseau en mouvement tire plus loin dans sa direction).

---

### 2.4 `WorldBounds.cs` — Dimensions du monde (US-10, US-12)

`readonly struct` avec `Width = 1920f`, `Height = 1080f`.

Choix d'un **struct readonly** : valeur sémantique (dimensions fixes), passage par `in` sans copie, zero-allocation.

---

## 3. Domain — Systèmes

```
Domain/
└── Systems/
    ├── PhysicsSystem.cs   ← NOUVEAU
    ├── WeaponSystem.cs    ← NOUVEAU
    └── DashSystem.cs      ← NOUVEAU
```

### 3.1 `PhysicsSystem.cs` — Physique avec inertie (US-08, US-12)

Système **sans état** (stateless) : toutes les données vivent dans les entités.

**Deux surcharges de `Tick()` :**

```
Tick(PhysicalEntity, deltaTime, bounds)   → pour astéroïdes et projectiles
Tick(Ship, thrustFwd, rotLeft, rotRight, deltaTime, bounds)  → pour vaisseaux contrôlés
```

**Pipeline d'une mise à jour de vaisseau :**
```
1. Rotation  += RotationSpeed × deltaTime  (si touche active)
2. Thrust dir = (sin(rot), -cos(rot))       (repère mathématique)
3. Velocity  += ThrustDir × ThrustForce × deltaTime
4. speed      = Velocity.Length()
   if speed > MaxSpeed → Velocity = Velocity/speed × MaxSpeed   (clamp)
5. Velocity  *= 0.99                        (drag → inertie)
6. Position  += Velocity × deltaTime
7. WrapAround(Position, bounds)             (toroïdal)
```

**Direction de poussée :**
```csharp
var thrustDir = new Vector2(MathF.Sin(rotation), -MathF.Cos(rotation));
```
- `Rotation = 0` → pointe vers le haut → `(sin0, -cos0) = (0, -1)` ✓
- `Rotation = π/2` → pointe à droite → `(sin90°, -cos90°) = (1, 0)` ✓

**Wrap-around toroïdal (US-12) :**
```csharp
if (pos.X < 0)           pos.X += bounds.Width;
else if (pos.X >= Width) pos.X -= bounds.Width;
// identique pour Y
```
Appliqué à **toutes** les entités physiques : vaisseaux, astéroïdes, projectiles.

---

### 3.2 `WeaponSystem.cs` — Tir de projectiles (US-09)

**`UpdateCooldown(ship, dt)`** : décrémente `WeaponCooldown` à chaque tick.

**`TryFire(ship, fireInput, nextId, hasRapidFire)`** :
- Conditions de refus : `!fireInput`, `cooldown > 0`, `!IsAlive`
- Spawn position = `ship.Position + direction × 20` (devant la pointe)
- Retourne `null` (pas de tir) ou un nouveau `Projectile`

Cooldowns :

| Mode | Valeur | Fréquence max |
|------|--------|---------------|
| Normal | 0.25s | 4 tirs/s |
| RapidFire (US-20) | 0.08s | 12.5 tirs/s |

---

### 3.3 `DashSystem.cs` — Boost temporaire (US-11)

**`Tick(ship, dashInput, dt)`** retourne `true` si un dash vient d'être déclenché (pour effets audio/visuels).

**Machine à états implicite :**
```
[Disponible]  ──dashInput──►  [Dashing 0.3s]  ──expiration──►  [Cooldown 3s]  ──0──►  [Disponible]
```

**Impulsion :** `ship.Velocity *= 2.5` — appliquée **une seule fois** au déclenchement, pas à chaque tick. La physique normale (drag 0.99) dissipe progressivement cette énergie.

**`GetCooldownProgress(ship)`** (méthode statique) : retourne `[0.0 – 1.0]` pour le HUD.

---

## 4. Shared — Paquet d'input

### `PlayerInputPacket.cs` — US-07, US-09, US-11

Format sur le fil : **9 octets total** (1 flags + 8 timestamp).

**Bitfield des 5 booléens :**

| Bit | Masque | Touche |
|-----|--------|--------|
| 0 | `0x01` | ThrustForward (W / ↑) |
| 1 | `0x02` | RotateLeft (A / ←) |
| 2 | `0x04` | RotateRight (D / →) |
| 3 | `0x08` | Fire (Espace / F) |
| 4 | `0x10` | Dash (Shift / E) |

Sans compactage, 5 booléens = 5 octets. Le bitfield réduit à 1 octet. À 60 Hz et 16 joueurs, l'économie est : `(5-1) × 60 × 16 = 3 840 octets/s` — non négligeable en UDP.

**`Timestamp`** (long, 8 octets) : `Stopwatch.ElapsedMilliseconds` côté client. Permet au serveur de détecter les paquets obsolètes (reçus hors ordre) et d'ignorer les inputs trop anciens.

---

## 5. Server — SpawnService

### `SpawnService.cs` — US-10

Algorithme de spawn sécurisé :

```
Pour i in 0..50:
    candidate = position aléatoire dans [margin, width-margin] × [margin, height-margin]
    minDist   = distance minimale à toutes les entités existantes
    if minDist >= 150 → retourner candidate  (zone sûre trouvée)
    if minDist > bestMinDist → mémoriser comme meilleure position
retourner bestPosition  (moins dangereuse trouvée)
```

**Paramètres :**

| Constante | Valeur | Rôle |
|-----------|--------|------|
| `SafeZoneRadius` | 150 u | Rayon de la zone de sécurité (US-10) |
| `MaxAttempts` | 50 | Plafond de tentatives avant fallback |
| `EdgeMargin` | 80 u | Marge bords pour éviter le spawn en bordure |

**Graine reproductible :** le constructeur accepte un `int? seed` optionnel. Utile pour les tests unitaires : `new SpawnService(WorldBounds.Default, seed: 42)` → positions déterministes.

---

## 6. Client — Gestion des inputs

```
Client/
└── Input/
    ├── PlayerInputState.cs   ← NOUVEAU
    └── InputHandler.cs       ← NOUVEAU
```

### 6.1 `PlayerInputState.cs` — Snapshot immutable

`readonly struct` (zero-allocation) exposant 5 `bool` + `IsAnyKeyPressed`.
Produit par `InputHandler.GetCurrentState()` à chaque frame.

---

### 6.2 `InputHandler.cs` — Capture clavier (US-07)

**Mapping des touches :**

| Action | Touches |
|--------|---------|
| ThrustForward | W · ↑ |
| RotateLeft | A · ← |
| RotateRight | D · → |
| Fire | Espace · F |
| Dash | Shift gauche · Shift droit · E |

**Thread-safety :** les événements `KeyDown`/`KeyUp` arrivent sur le UI thread Avalonia, mais `GetCurrentState()` est appelé depuis le `DispatcherTimer` (UI thread aussi en l'occurrence). Le `lock` est présent par précaution et pour une future migration vers un thread de jeu dédié.

**Nettoyage sur perte de focus :** `ClearAll()` remet à zéro le `HashSet<Key>` — à appeler depuis `GameView.OnLostFocus` dans un futur refactor (BLOC 7).

**Attachement :** `InputHandler` s'abonne sur le `TopLevel` (la fenêtre), pas sur le `UserControl`, pour capturer les touches même si le focus est sur un enfant.

---

## 7. Client — GameViewModel

### `GameViewModel.cs` — US-07, US-11, US-29

**Boucle de jeu client à ~60 Hz :**

```
DispatcherTimer (16ms) → OnGameTick()
    │
    ├─ PollEvents()                         (traiter les paquets réseau en attente)
    ├─ inputState = inputHandler.GetCurrentState()
    ├─ SendUnreliable(PlayerInputPacket)     (envoi UDP au serveur)
    └─ UpdateDashPrediction(inputState, dt)  (mise à jour HUD local)
```

**Prédiction locale du dash (US-11) :**
Le serveur est autoritaire, mais le cooldown du dash est prédit localement pour un retour visuel immédiat. Si le serveur corrige (snapshot différent), les valeurs seront réconciliées (BLOC 6).

```
dashJustPressed && cooldown == 0  →  cooldown = 3s  (prédiction)
cooldown > 0                      →  cooldown -= deltaTime
DashCooldownProgress = 1 - cooldown/3           → binding barre HUD
IsDashReady = cooldown <= 0                     → binding couleur
```

**Propriétés HUD exposées (US-29) :**

| Propriété | Type | Alimentation |
|-----------|------|-------------|
| `AlivePlayersCount` | `int` | Snapshots serveur (BLOC 6) |
| `MyScore` | `int` | Événements kills (BLOC 5) |
| `MyRank` | `int` | Snapshots serveur (BLOC 6) |
| `DashCooldownProgress` | `double` | Prédiction locale |
| `IsDashReady` | `bool` | Prédiction locale |

**Cycle de vie :**
- Créé par `App.axaml.cs` via la factory DI
- `AttachInputHandler()` appelé par `GameView.OnAttachedToVisualTree`
- `Dispose()` appelé par `GameView.OnDetachedFromVisualTree` → arrêt timer + détachement InputHandler

---

## 8. Client — GameView AXAML

### `GameView.axaml` + `GameView.axaml.cs`

**Structure de la vue :**
```
Grid
├── Canvas "GameCanvas"   (fond noir #05050F, rendu entités — BLOC 7)
└── Panel HUD (IsHitTestVisible=False → le clic traverse vers le Canvas)
    ├── [Top-Left]  Joueurs vivants (AlivePlayersCount)
    ├── [Top-Right] Score + Rang (MyScore, MyRank)
    └── [Bot-Left]  Barre de recharge dash (DashCooldownProgress + IsDashReady)
```

**`OnAttachedToVisualTree`** — Point d'injection de l'InputHandler :
```csharp
var topLevel = TopLevel.GetTopLevel(this);
if (topLevel is not null)
    vm.AttachInputHandler(topLevel);
```
`TopLevel.GetTopLevel()` est l'API Avalonia 11 recommandée pour accéder à la fenêtre racine depuis n'importe quel `UserControl`.

**`OnDetachedFromVisualTree`** — Nettoyage :
```csharp
if (DataContext is GameViewModel vm) vm.Dispose();
```
Garantit que le timer de jeu s'arrête si la vue est retirée (navigation vers lobby/GameOver).

---

## 9. Client — Convertisseurs HUD

### `BoolToColorConverter`
Paramètre XAML `"#ColorTrue|#ColorFalse"` → `SolidColorBrush`.
Utilisé pour la couleur de la barre dash : cyan si prêt, bleu sombre si en recharge.

### `ProgressToWidthConverter`
Paramètre XAML = largeur max en pixels (ex. `"136"`).
`progress [0.0–1.0] × maxWidth → double` bindé sur `Width` de l'indicateur interne.
Simule une `ProgressBar` sans utiliser le contrôle Avalonia (plus de contrôle visuel).

---

## 10. Mises à jour des fichiers existants

### `LobbyViewModel.cs`
```csharp
// Avant
// La navigation vers GameViewModel sera implémentée dans BLOC 7.

// Après
_navigationService.NavigateTo<GameViewModel>();
```
La navigation vers l'écran de jeu est désormais opérationnelle dès que `SecondsRemaining == 0`.

### `App.axaml.cs`
```csharp
if (type == typeof(GameViewModel))
    return new GameViewModel(_networkService!, _navigationService!);
```
`GameViewModel` est enregistré dans la factory DI.

---

## 11. Architecture & flux de données

### Flux complet d'un input clavier

```
[Joueur appuie sur W]
        │
        ▼ KeyDown event (UI thread Avalonia)
InputHandler._pressedKeys.Add(Key.W)
        │
        ▼ DispatcherTimer tick (16ms, UI thread)
GameViewModel.OnGameTick()
    │ inputHandler.GetCurrentState()
    │  → PlayerInputState { ThrustForward=true, ... }
    │
    ▼ SendUnreliable(PlayerInputPacket)
LiteNetClientService
    │ Sérialise : [0x02][0x01][timestamp 8 octets]
    │
    ▼ UDP → Serveur
GameLoop.ProcessInput(playerId, packet)
    │ PhysicsSystem.Tick(ship, thrustFwd=true, ...)
    │  → ship.Velocity += thrustDir × 300 × dt
    │  → ship.Velocity *= 0.99
    │  → ship.Position += velocity × dt
    │  → WrapAround(ship.Position)
    │
    ▼ Broadcast GameStateSnapshot (UDP, 20 Hz)
LiteNetClientService.OnNetworkReceive()
    │
    ▼ GameViewModel.HandleGameStateSnapshot()  [BLOC 6]
EntityInterpolator → rendu interpolé
```

---

### Graphe de dépendances (après BLOC 2)

```
Domain: PhysicalEntity, Ship, Projectile, WorldBounds
        PhysicsSystem, WeaponSystem, DashSystem
    ▲
Shared: PlayerInputPacket (+ BLOC 1)
    ▲                      ▲
GameLogic (interfaces)  Server: SpawnService
    ▲
Infrastructure: LiteNetClientService
    ▲
Client: InputHandler, PlayerInputState
        GameViewModel, GameView
        BoolToColorConverter, ProgressToWidthConverter
```

---

## 12. Décisions techniques & compromis

### 12.1 Système sans état vs système avec état

**Décision :** `PhysicsSystem`, `WeaponSystem`, `DashSystem` sont **sans état** (stateless).

**Raison :** la simulation déterministe 60 Hz du serveur doit pouvoir avancer N ticks d'un coup
(rattrapage de retard réseau) sans effet de bord. Un système sans état est trivialement
parallélisable et testable unitairement (pas de `Setup`, pas de `Teardown`).

**Compromis :** les systèmes ne peuvent pas maintenir de cache interne. Si un calcul coûteux
était répété (ex. BVH spatial), il faudrait le déporter dans une structure externe.

---

### 12.2 `DispatcherTimer` vs thread dédié

**Décision :** boucle de jeu client sur `DispatcherTimer` (UI thread).

**Raison :** Avalonia interdit les modifications de propriétés observables hors UI thread
sans `Dispatcher.UIThread.Post()`. Un timer sur le UI thread simplifie le code (pas de marshaling).

**Compromis :** à 60 Hz, chaque tick dispose de ~16 ms pour s'exécuter. Si le rendu
ou la désérialisation prend trop de temps, des sauts de frame apparaissent. Acceptable
pour un prototype ; un thread de jeu dédié avec `Dispatcher.UIThread.Post()` pour les
propriétés bindées serait la solution scalable (BLOC 7+).

---

### 12.3 Prédiction locale du dash

**Décision :** le cooldown du dash est prédit localement dans `GameViewModel`.

**Raison :** sans prédiction, l'indicateur de recharge aurait 50–150 ms de lag (latence réseau),
ce qui rendrait le feedback utilisateur désagréable. La prédiction donne un retour immédiat.

**Compromis :** si la latence est élevée ou si un paquet est perdu, la prédiction peut
diverger légèrement du serveur. La réconciliation (correction par snapshot — BLOC 6)
résout cette divergence de façon transparente.

---

### 12.4 Spawn offset du projectile

**Décision :** le projectile spawn à `ship.Position + direction × 20`.

**Raison :** sans offset, le projectile spawne au centre du vaisseau et détecterait
immédiatement une collision avec lui (cercle de 16 px vs projectile de 4 px).
L'offset de 20 unités place le projectile juste devant la pointe.

---

## 13. Points d'attention pour la suite

### Pour BLOC 3 (Astéroïdes — requis pour jouer)

- **`Asteroid.cs`** : tailles Large/Medium/Small avec leurs paramètres (rayon, PV, vitesse)
- **`CollisionSystem.cs`** : détection cercle-cercle entre toutes les paires d'entités
- **`AsteroidSpawnService`** : utiliser `SpawnService` comme base, adapter pour les bords
- **Fragmentation** : `AsteroidDestroyedEvent` avec spawn des fragments fils

### Pour le rendu (requis pour jouer)

- Le `GameCanvas` est vide — il faut des `DrawingContext` ou des enfants `Polygon`/`Ellipse`
  pour afficher vaisseaux, astéroïdes et projectiles
- Les snapshots serveur (BLOC 6) alimenteront la liste d'entités à rendre

### TODOs explicites dans le code

| Fichier | Ligne | TODO |
|---------|-------|------|
| `GameViewModel.cs` | `HandlePlayerEliminated` | Désérialiser et afficher dans le feed (BLOC 5) |
| `GameViewModel.cs` | `HandleGameOver` | Naviguer vers GameOverViewModel (BLOC 5) |
| `GameView.axaml.cs` | — | Appeler `ClearAll()` sur perte de focus |

---

*Document généré le 2026-04-03 — AsteroidOnline BLOC 2*
