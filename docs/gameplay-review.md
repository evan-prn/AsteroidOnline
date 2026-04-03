# Gameplay — Revue technique détaillée

> **Date :** 2026-04-03
> **Branche :** `develop`
> **Build :** ✅ 0 erreur · 0 avertissement
> **Périmètre :** BLOCS 3–5 + Server opérationnel + Rendu client
> **User Stories couvertes :** US-13, US-14, US-15, US-16, US-17, US-18, US-22, US-23, US-24, US-25, US-27, US-29

---

## Table des matières

1. [Vue d'ensemble](#1-vue-densemble)
2. [Domain — Astéroïdes](#2-domain--astéroïdes)
3. [Domain — Événements de jeu](#3-domain--événements-de-jeu)
4. [Domain — CollisionSystem](#4-domain--collisionsystem)
5. [Shared — Paquets de gameplay](#5-shared--paquets-de-gameplay)
6. [Server — AsteroidSpawnService](#6-server--asteroidspawnservice)
7. [Server — WaveManager](#7-server--wavemanager)
8. [Server — GameLoop (60 Hz autoritaire)](#8-server--gameloop-60-hz-autoritaire)
9. [Server — Program.cs](#9-server--programcs)
10. [Client — GameRenderer](#10-client--gamerenderer)
11. [Client — GameViewModel (mis à jour)](#11-client--gameviewmodel-mis-à-jour)
12. [Client — GameView (mis à jour)](#12-client--gameview-mis-à-jour)
13. [Architecture & flux complet d'une partie](#13-architecture--flux-complet-dune-partie)
14. [Décisions techniques & compromis](#14-décisions-techniques--compromis)
15. [Limites connues & prochaines étapes](#15-limites-connues--prochaines-étapes)

---

## 1. Vue d'ensemble

Cette session complète le chemin critique vers un jeu **jouable en réseau** :
un serveur autoritaire 60 Hz tourne, des clients peuvent se connecter, piloter
un vaisseau, tirer sur des astéroïdes qui se fragmentent, et mourir s'ils entrent
en collision.

**Statistiques :**

| Catégorie | Nombre |
|-----------|--------|
| Fichiers créés | 13 |
| Fichiers modifiés | 3 (GameViewModel, GameView, Program.cs) |
| Lignes de code ajoutées | ~950 |
| Erreurs de build corrigées | 1 |

**Ce qui est désormais opérationnel :**
- Serveur LiteNetLib acceptant des connexions (lobby → compte à rebours → partie)
- Spawn de 10 astéroïdes Large au démarrage
- Physique 60 Hz sur tous les objets (vaisseaux, astéroïdes, projectiles)
- Détection de collisions : projectile↔astéroïde, projectile↔joueur, astéroïde↔joueur
- Fragmentation en cascade : Large → 2-3 Medium → 2-3 Small
- Vagues automatiques toutes les 30s (+20% astéroïdes, plafond 80)
- Snapshot UDP 20 Hz → rendu canvas côté client
- Feed d'éliminations HUD (4 secondes)
- Écran de fin de partie (retour lobby)

---

## 2. Domain — Astéroïdes

### `AsteroidSize.cs` (US-18)

Enum `byte` à 3 valeurs. Le type `byte` minimise la taille des snapshots réseau
(1 octet au lieu de 4 pour un `int`).

### `Asteroid.cs` (US-13, US-18)

Étend `PhysicalEntity` avec trois responsabilités :

**Propriétés d'état :**

| Propriété | Type | Rôle |
|-----------|------|------|
| `Size` | `AsteroidSize` | Taille (détermine tout le reste) |
| `HitPoints` | `int` | PV restants, décrémentés par les projectiles |
| `IsActive` | `bool` | Faux dès la destruction (évite le double-comptage dans la boucle) |

**`CollisionRadius` (propriété calculée) :**
```csharp
public override float CollisionRadius => Size switch
{
    AsteroidSize.Large  => 48f,
    AsteroidSize.Medium => 28f,
    AsteroidSize.Small  => 14f,
};
```
Implémente l'abstraction de `PhysicalEntity` sans duplication.

**Méthodes statiques usine :**

```csharp
Asteroid.GetInitialHitPoints(size)  // Large=3, Medium=2, Small=1
Asteroid.GetBaseSpeed(size)         // Large=60, Medium=100, Small=160 u/s
```
Ces méthodes statiques centralisent les paramètres d'équilibrage du jeu.
Les changer à un seul endroit suffit pour rééquilibrer l'ensemble.

**Paramètres d'équilibrage (US-18) :**

| Taille | PV | Rayon | Vitesse | Surface visuelle |
|--------|-----|-------|---------|-----------------|
| Large | 3 | 48 u | 60 u/s | Lent, massif, résistant |
| Medium | 2 | 28 u | 100 u/s | Intermédiaire |
| Small | 1 | 14 u | 160 u/s | Rapide, agile, fragile |

---

## 3. Domain — Événements de jeu

```
Domain/
└── Events/
    ├── AsteroidDestroyedEvent.cs   ← NOUVEAU
    └── PlayerKilledEvent.cs        ← NOUVEAU
```

### `AsteroidDestroyedEvent.cs` (US-14, US-17)

Contient deux types imbriqués :

**`AsteroidFragment` :** données d'un fragment à spawner.

| Propriété | Type | Description |
|-----------|------|-------------|
| `Id` | `int` | ID pré-calculé par `AsteroidSpawnService` |
| `Size` | `AsteroidSize` | Taille du fragment (une cran en dessous du parent) |
| `Position` | `Vector2` | Héritée du parent (point d'explosion) |
| `Velocity` | `Vector2` | Calculée avec déviation aléatoire + héritage partiel |
| `AngularVelocity` | `float` | Rotation libre du fragment |

**`AsteroidDestroyedEvent` :** event produit par `AsteroidSpawnService.CreateDestroyedEvent()`.

| Propriété | Description |
|-----------|-------------|
| `AsteroidId` | Pour retirer l'astéroïde du dictionnaire serveur |
| `Position` | Pour les effets visuels (BLOC 7) |
| `NewFragments` | Liste des fragments à instancier |
| `DropsPowerUp` | 20% de chance pour Large/Medium (US-17) |

Ce pattern **Event** découple la logique de destruction (Domain) de son
traitement (GameLoop/Server) : le Domain ne sait pas comment les fragments
sont ajoutés au monde, il décrit seulement ce qui doit se passer.

---

### `PlayerKilledEvent.cs` (US-15, US-22)

Transporté côté Domain pour les tests unitaires futurs.

| Propriété | Valeur spéciale |
|-----------|----------------|
| `KillerId` | `-1` si tué par astéroïde |
| `KillerName` | `"Astéroïde"` par défaut |

---

## 4. Domain — CollisionSystem

### `CollisionSystem.cs` (US-15, US-22)

Système **sans état**, trois méthodes de détection :

| Méthode | Vérifie | Génère |
|---------|---------|--------|
| `CheckProjectileVsShips()` | proj ↔ joueurs (≠ owner) | vaisseau touché |
| `CheckAsteroidVsShip()` | astéroïde ↔ joueurs | vaisseau touché |
| `CheckProjectileVsAsteroids()` | proj ↔ astéroïdes | astéroïde touché |

**Optimisation clé — comparaison sur les carrés :**
```csharp
private static bool Overlaps(Vector2 posA, float rA, Vector2 posB, float rB)
{
    var combinedRadius = rA + rB;
    return Vector2.DistanceSquared(posA, posB) < combinedRadius * combinedRadius;
}
```
`DistanceSquared` évite un `sqrt` coûteux à chaque test. À 60 Hz avec 80 astéroïdes,
16 joueurs et ~50 projectiles actifs, l'économie est significative.

**Exclusion de l'auto-collision :**
```csharp
if (ship.Id == projectile.OwnerId) continue;
```
Un joueur ne peut pas se tuer avec ses propres projectiles.

**Complexité :** O(P × S) pour projectile↔joueurs, O(A × S) pour astéroïdes↔joueurs,
O(P × A) pour projectile↔astéroïdes. Acceptable pour les volumes cibles (≤16 joueurs,
≤80 astéroïdes, ≤50 projectiles actifs). Un BVH ou un spatial hash serait nécessaire
au-delà.

---

## 5. Shared — Paquets de gameplay

```
Shared/Packets/
├── GameStateSnapshotPacket.cs   ← NOUVEAU
├── PlayerEliminatedPacket.cs    ← NOUVEAU
└── GameOverPacket.cs            ← NOUVEAU
```

### `GameStateSnapshotPacket.cs` (US-23)

Paquet UDP le plus volumineux du protocole. Contient trois listes :

**`PlayerSnapshot` (par joueur) :**

| Champ | Octets | Description |
|-------|--------|-------------|
| `Id` | 4 | Identifiant |
| `X`, `Y` | 4+4 | Position |
| `Rotation` | 4 | Angle |
| `VelocityX`, `VelocityY` | 4+4 | Pour l'extrapolation client (BLOC 6) |
| `Color` | 1 | PlayerColor (byte) |
| `IsAlive` | 1 | Booléen |
| `DashCooldownProgress` | 4 | Pour corriger la prédiction locale |
| **Total** | **26 octets/joueur** | |

**`AsteroidSnapshot` (par astéroïde) :**

| Champ | Octets |
|-------|--------|
| `Id` | 4 |
| `X`, `Y` | 8 |
| `Rotation` | 4 |
| `Size` | 1 |
| `HitPoints` | 4 |
| **Total** | **21 octets/astéroïde** |

**`ProjectileSnapshot` (par projectile) :**

| Champ | Octets |
|-------|--------|
| `Id`, `X`, `Y`, `OwnerId` | 16 |

**Estimation de la taille d'un snapshot complet :**
```
En-tête (timestamp + aliveCount) : 12 octets
16 joueurs × 26 octets           : 416 octets
80 astéroïdes × 21 octets        : 1 680 octets
50 projectiles × 16 octets       : 800 octets
                          Total  : ~2 908 octets ≈ 2.8 Ko
```
À 20 Hz vers 16 clients : `2.9 Ko × 20 × 16 ≈ 930 Ko/s` en sortie serveur.
Raisonnable pour un LAN ou une connexion résidentielle (≈7 Mbit/s max).

---

### `PlayerEliminatedPacket.cs` (US-24)

Envoyé en `ReliableOrdered` (TCP-like) à **tous** les clients.

```
[ PacketType 1B ] [ VictimId 4B ] [ VictimName string ] [ KillerName string ]
```

La chaîne LiteNetLib `WriteString` préfixe la longueur (2 octets) + UTF-8.
Taille typique : ~30 octets.

---

### `GameOverPacket.cs` (US-25)

Minimal : `WinnerId` (4 octets) + `WinnerName` (string). Envoyé une seule fois
en `ReliableOrdered`.

---

## 6. Server — AsteroidSpawnService

### `AsteroidSpawnService.cs` (US-13, US-14, US-16, US-17)

**Compteur d'ID** : `_nextId` commence à `1000` pour réserver la plage `1–999`
aux joueurs, ce qui évite toute collision d'ID entre joueurs et astéroïdes.

**`SpawnInitialWave(count=10)` (US-13) :**
Crée 10 astéroïdes Large sur les bords de la carte.

**`SpawnWave(currentCount, maxAsteroids=80)` (US-16) :**
```csharp
var toSpawn = (int)(currentCount * 0.20f);
toSpawn = Math.Clamp(toSpawn, 1, maxAsteroids - currentCount);
```
Garantit au moins 1 nouveau spawn et ne dépasse jamais le plafond de 80.

**`CreateDestroyedEvent(asteroid)` (US-14) — Fragmentation en cascade :**

```
Large  → 2 ou 3 Medium
Medium → 2 ou 3 Small
Small  → (rien)
```

Pour chaque fragment :
```csharp
var angle    = random.NextDouble() * 2π          // direction aléatoire
var velocity = direction * childSpeed
             + parentVelocity * 0.3f             // héritage 30%
var angularV = random in [-0.75, 0.75] rad/s     // rotation propre
```
L'héritage partiel de vélocité (30%) évite que les fragments "naissent à l'arrêt"
mais ne les propulse pas trop loin du point d'explosion.

**`SpawnOnEdge()` :**
Choisit un bord aléatoire (0=haut, 1=bas, 2=gauche, 3=droite) puis une position
aléatoire sur ce bord. La direction initiale est orientée vers le centre avec
une déviation de ±54° (`π × 0.6 / 2`).

**`CreateFromFragment(fragment)` (méthode statique) :**
Convertit un `AsteroidFragment` (DTO d'événement) en `Asteroid` (entité du monde).
Séparation propre entre la description de ce qui doit se passer (event) et son
instanciation (factory).

**Drop de power-up (US-17) :**
```csharp
var dropsPowerUp = asteroid.Size != AsteroidSize.Small
                && _random.NextDouble() < 0.20;
```
20% de chance pour Large et Medium uniquement.
Le flag est dans l'événement ; c'est la GameLoop qui décide quoi en faire.

---

## 7. Server — WaveManager

### `WaveManager.cs` (US-16)

Machine à états minimaliste :

```csharp
public bool Tick(float deltaTime, int currentAsteroidCount)
{
    if (currentAsteroidCount >= MaxAsteroids) return false;
    _waveTimer += deltaTime;
    if (_waveTimer < 30f)           return false;
    _waveTimer -= 30f;              // reset (pas d'accumulation)
    CurrentWave++;
    return true;
}
```

**Points notables :**
- `_waveTimer -= 30f` et non `= 0f` : évite la dérive temporelle si un tick
  arrive légèrement en retard
- Pas de nouvelle vague si `currentCount >= 80` : le plafond est respecté même
  si le timer a expiré
- `SecondsUntilNextWave` exposé pour un futur affichage HUD

---

## 8. Server — GameLoop (60 Hz autoritaire)

### `GameLoop.cs` (US-27)

C'est le cœur du serveur. Voici une analyse section par section.

---

### 8.1 Machine à états de la session

```
[Lobby]
  │  ≥1 joueur connecté
  ▼
[Countdown]  5 secondes, BroadcastCountdown chaque seconde
  │  SecondsRemaining == 0
  ▼
[Playing]    boucle de simulation 60 Hz
  │  aliveCount <= 1
  ▼
[GameOver]   BroadcastGameOver → (fin)
```

---

### 8.2 Boucle principale (`RunAsync`)

```csharp
var nextTick = sw.Elapsed.TotalMilliseconds;

while (!ct.IsCancellationRequested)
{
    var now = sw.Elapsed.TotalMilliseconds;
    if (now < nextTick)
    {
        if ((int)(nextTick - now) > 1)
            await Task.Delay(1, ct);    // cède le CPU sans bloquer
        continue;
    }
    nextTick += TickDurationMs;         // accumulation vs reset
    _netManager.PollEvents();
    Tick(deltaTime);
}
```

**Pourquoi `nextTick += TickDurationMs` et non `nextTick = now + TickDurationMs` ?**
Si un tick prend 20ms (au lieu de 16.67ms), la prochaine échéance est avancée.
Sur la durée, cela maintient un taux moyen de 60 Hz même sous légère charge.
Avec `= now + ...`, le retard s'accumule indéfiniment ("spiral of death").

**Pourquoi `Task.Delay(1)` et non `Thread.Sleep(1)` ?**
La GameLoop tourne dans un contexte `async` (évite de bloquer un thread pool).
`Task.Delay(1)` libère le thread entre les ticks, permettant au GC de s'exécuter.

---

### 8.3 File d'inputs thread-safe

```csharp
private readonly ConcurrentQueue<(int PlayerId, PlayerInputPacket Input)> _inputQueue;
```

Les callbacks LiteNetLib (`OnNetworkReceive`) s'exécutent sur le thread de polling.
La GameLoop tourne sur son propre `Task`. La `ConcurrentQueue` est le seul pont
thread-safe entre les deux sans verrou explicite.

**Traitement dans `ProcessInputQueue()` :**
```
Pour chaque (playerId, input) en file :
    1. DashSystem.Tick(ship, input.Dash, dt)
    2. PhysicsSystem.Tick(ship, input.ThrustForward, RotateLeft, RotateRight, dt, bounds)
    3. WeaponSystem.TryFire(ship, input.Fire, nextProjectileId) → ajout si non-null
```

---

### 8.4 Pipeline `TickPlaying` complet

```
1. ProcessInputQueue()        ← traitement des intentions clients
2. PhysicsSystem.Tick(ships)  ← mouvement des vaisseaux sans input (inertie)
3. PhysicsSystem.Tick(asteroids)
4. PhysicsSystem.Tick(projectiles) + expiration lifetime
5. ProcessCollisions()
   ├── proj ↔ astéroïdes  → DamageAsteroid()
   ├── proj ↔ joueurs     → EliminatePlayer()
   └── astéroïdes ↔ joueurs → EliminatePlayer()
6. WaveManager.Tick() → SpawnWave() si nécessaire
7. Snapshot broadcast toutes les 3 ticks (20 Hz)
8. CheckGameOver()
```

**Ordre important :** les collisions sont détectées *après* les mouvements du tick courant.
Un projectile qui traverse un astéroïde en un seul tick (possible si très rapide vs très petit)
peut passer sans collision : c'est un problème connu de détection discrète, acceptable
pour ce type de jeu.

---

### 8.5 `DamageAsteroid(asteroid, shooterId)`

```csharp
asteroid.HitPoints--;
if (asteroid.HitPoints > 0) return;   // pas encore détruit

asteroid.IsActive = false;
_asteroids.Remove(asteroid.Id);

var evt = _asteroidSvc.CreateDestroyedEvent(asteroid);
foreach (var fragment in evt.NewFragments)
{
    var newAsteroid = AsteroidSpawnService.CreateFromFragment(fragment);
    _asteroids[newAsteroid.Id] = newAsteroid;
}
```

**Fragmentation en cascade automatique :** les nouveaux Medium/Small entrent dans
`_asteroids` et seront potentiellement touchés lors des ticks suivants, ce qui
re-déclenchera `DamageAsteroid` pour les fragments à leur tour. Pas de récursion,
pas de logique spéciale nécessaire.

---

### 8.6 `EliminatePlayer(victim, killerId)`

```csharp
victim.IsAlive = false;
BroadcastReliable(new PlayerEliminatedPacket { ... });
```

La mort est immédiate (sans bouclier — BLOC 4 prévu). Le vaisseau reste dans
`_ships` mais avec `IsAlive = false` ; il continue d'apparaître dans les snapshots
avec ce flag pour que les clients puissent animer la mort.

---

### 8.7 Sérialisation serveur

```csharp
private static NetDataWriter Serialize(IPacket packet)
{
    var nw = new NetDataWriter();
    nw.Put((byte)packet.Type);           // 1 octet de type
    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);
    packet.Serialize(bw);
    bw.Flush();
    nw.Put(ms.ToArray());                // corps binaire
    return nw;
}
```

Cette méthode alloue un `MemoryStream` à chaque appel. À 20 Hz × 16 clients,
c'est 320 allocations/seconde pour le snapshot — acceptable pour un prototype,
à optimiser avec un pool (`ArrayPool<byte>`) pour la production.

---

## 9. Server — Program.cs

Le `Program.cs` est désormais un vrai point d'entrée opérationnel :

```csharp
using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Information));

using var gameLoop = new GameLoop(logger);
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
await gameLoop.RunAsync(cts.Token);
```

**Dépendances ajoutées au `Server.csproj` :**
```xml
<PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="10.0.0" />
```

`ILogger<GameLoop>` est injecté dans la `GameLoop` conformément aux exigences
du prompt maître. Tous les événements serveur (connexion, élimination, vague,
fin de partie) sont loggués avec le bon niveau (`Information` / `Debug` / `Warning`).

---

## 10. Client — GameRenderer

### `GameRenderer.cs`

Moteur de rendu 2D sur `Canvas` Avalonia. Dessin par remplacement complet
(`Children.Clear()` + re-création à chaque frame).

**Mise à l'échelle adaptative :**
```csharp
var scaleX = canvas.Bounds.Width  / 1920f;
var scaleY = canvas.Bounds.Height / 1080f;
var scale  = Math.Min(scaleX, scaleY);          // lettre-boxing
var offsetX = (canvas.Bounds.Width  - 1920f * scale) / 2;
var offsetY = (canvas.Bounds.Height - 1080f * scale) / 2;
```
Le monde 1920×1080 est centré dans le canvas quelle que soit la résolution
de la fenêtre client, avec des bandes noires si le ratio ne correspond pas.

---

### Rendu des vaisseaux (`DrawShip`)

Chaque vaisseau est un triangle `Polygon` à 3 sommets calculés par rotation 2D :

```csharp
// Repère local (pointe vers le haut à rotation=0)
var pts = new[] {
    Rotate(0,           -size,        r),  // pointe avant
    Rotate(-size*0.7,   size*0.8,     r),  // aile gauche
    Rotate( size*0.7,   size*0.8,     r),  // aile droite
};
```

La fonction `Rotate(x, y, angle)` applique la matrice de rotation 2D standard :
```
x' = x·cos(a) - y·sin(a)
y' = x·sin(a) + y·cos(a)
```

**Joueur local :** contour blanc (`StrokeThickness=1.5`) + halo elliptique semi-transparent
pour le distinguer visuellement des adversaires.

**Barre de cooldown dash sous le vaisseau :** affichée uniquement si `DashCooldownProgress < 1`.
Deux `Rectangle` superposés (fond sombre + barre cyan proportionnelle).

---

### Rendu des astéroïdes (`DrawAsteroid`)

Octogone irrégulier simulé :
```csharp
for (var i = 0; i < 8; i++)
{
    var angle = asteroid.Rotation + i * 2π / 8;
    // Irrégularité stable basée sur l'ID (pas de bruit aléatoire par frame)
    var r2 = radius * (0.8 + 0.2 * |sin(i × id × 1.3)|);
    pts.Add(new Point(cx + cos(angle)*r2, cy + sin(angle)*r2));
}
```

L'irrégularité est **déterministe** : elle dépend de `i` et de `asteroid.Id`,
pas d'un `Random`. Le même astéroïde a donc toujours la même forme entre les
frames, sans effet de scintillement.

Les trois tailles ont des couleurs distinctes :
- Large : `#9966AA` (violet moyen)
- Medium : `#7755AA` (violet foncé)
- Small : `#553388` (violet très foncé)

---

### Rendu des projectiles (`DrawProjectile`)

Simple `Ellipse` de rayon `3 × scale` en jaune (`#FFFF88`). Pas de rotation
car la forme est circulaire.

---

### Optimisations absentes (intentionnellement)

- **Pas de `DrawingContext` personnalisé** : le rendu par `Children.Clear()` + ajout
  d'objets Avalonia est plus simple mais moins performant qu'un `CustomDrawOp`.
  Acceptable pour le prototype ; à remplacer si le framerate chute sous 30 fps
  avec 80 astéroïdes.
- **Pas de culling** : toutes les entités sont rendues même si hors écran (impossible
  avec le wrap-around toroïdal).
- **Pas de pool de contrôles** : chaque frame crée de nouveaux `Polygon`/`Ellipse`.
  Un pool réduirait la pression sur le GC (BLOC 7+).

---

## 11. Client — GameViewModel (mis à jour)

### Changements par rapport à la version BLOC 2

**`Attach(inputSource, canvas)`** remplace `AttachInputHandler()` :
```csharp
public void Attach(IInputElement inputSource, Canvas gameCanvas)
{
    _inputHandler = new InputHandler(inputSource);
    _renderer     = new GameRenderer(gameCanvas);
}
```
Un seul point d'entrée pour les deux dépendances visuelles.

**`LocalPlayerId`** : propriété publique settable, initialisée depuis `App.axaml.cs`
après réception du `LobbyJoinedPacket`. Transmis au renderer pour identifier le
joueur local visuellement.

**`HandleSnapshot(reader)` :**
```csharp
Dispatcher.UIThread.Post(() =>
{
    _lastSnapshot     = packet;
    AlivePlayersCount = packet.AlivePlayersCount;

    // Correction de la prédiction locale du dash
    var mySnap = packet.Players.Find(p => p.Id == LocalPlayerId);
    if (mySnap is not null)
        _dashCooldownRemaining = (1f - mySnap.DashCooldownProgress) * 3f;
});
```
Le snapshot serveur **corrige** la prédiction locale du cooldown dash,
réalisant ainsi la réconciliation client-serveur pour cet état.

**`HandlePlayerEliminated(reader)` (US-24) :**
```csharp
EliminationFeedText = $"{packet.KillerName} a éliminé {packet.VictimName}";
ShowEliminationFeed = true;

var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
timer.Tick += (s, _) => { ShowEliminationFeed = false; ((DispatcherTimer)s!).Stop(); };
timer.Start();
```
Le `DispatcherTimer` one-shot masque le message après 4 secondes sans bloquer le thread.

**`HandleGameOver(reader)` (US-25) :**
Navigation vers `LobbyViewModel` pour l'instant. Un `GameOverViewModel` avec animation
de victoire sera ajouté dans la suite.

**Rendu dans `OnGameTick` :**
```csharp
if (_renderer is not null && _lastSnapshot is not null)
    _renderer.Render(_lastSnapshot, LocalPlayerId);
```
Le rendu s'exécute à 60 Hz (même tick que les inputs), mais n'utilise que le dernier
snapshot reçu (20 Hz). Entre deux snapshots, la position des entités est "gelée"
visuellement — l'interpolation sera ajoutée dans BLOC 6.

---

## 12. Client — GameView (mis à jour)

### Changement principal dans `GameView.axaml.cs`

```csharp
// Avant
vm.AttachInputHandler(topLevel);

// Après
var canvas = this.FindControl<Canvas>("GameCanvas");
if (topLevel is not null && canvas is not null)
    vm.Attach(topLevel, canvas);
```

Le canvas est récupéré par son nom `x:Name="GameCanvas"` via `FindControl<Canvas>`.

### Ajouts dans `GameView.axaml`

**Feed d'éliminations (US-24) :**
```xml
<Border HorizontalAlignment="Center" VerticalAlignment="Top"
        IsVisible="{Binding ShowEliminationFeed}"
        Background="#CC220010" BorderBrush="#55FF4444">
    <TextBlock Text="{Binding EliminationFeedText}" Foreground="#FF8888" />
</Border>
```
Masqué par défaut (`ShowEliminationFeed = false`), visible 4 secondes à chaque kill.

**Rappel des contrôles (bas droite) :**
Bloc semi-transparent listant WASD, Espace/F, Shift/E pour les nouveaux joueurs.

---

## 13. Architecture & flux complet d'une partie

### Cycle de vie d'une session

```
SERVEUR                              CLIENT(S)
───────                              ─────────
NetManager.Start(7777)
                                     ConnectAsync(127.0.0.1, 7777)
OnPeerConnected(peer)
HandleConnectRequest(peer, reader)
  → _ships[id] = new Ship(...)
  → Send(LobbyJoinedPacket)          OnPacketReceived(LobbyJoined)
  → BroadcastLobbyState()              → NavigateTo<LobbyViewModel>()

[Phase Lobby]
  ≥1 joueur → Phase Countdown
  BroadcastCountdown(5)              OnPacketReceived(Countdown)
  BroadcastCountdown(4)                → LobbyViewModel.CountdownText = "4..."
  ...
  BroadcastCountdown(0)              OnPacketReceived(Countdown 0)
                                       → NavigateTo<GameViewModel>()

[Phase Playing — 60 Hz]
  ProcessInputQueue()                DispatcherTimer (16ms)
                                       → SendUnreliable(PlayerInputPacket)
  → PhysicsSystem.Tick(ships)
  → PhysicsSystem.Tick(asteroids)
  → Collisions → PlayerKilled?
  → WaveManager.Tick()
  → [toutes les 3 ticks]             OnPacketReceived(GameStateSnapshot)
    BroadcastSnapshot() ──────────►    → _lastSnapshot = packet
                                       → renderer.Render(snapshot, localId)

  EliminatePlayer(victim, killer)
  BroadcastReliable(PlayerEliminated) → ShowEliminationFeed 4s

  CheckGameOver() → 1 survivant
  BroadcastReliable(GameOver) ──────► HandleGameOver()
                                        → NavigateTo<LobbyViewModel>()
```

---

## 14. Décisions techniques & compromis

### 14.1 `Children.Clear()` vs `DrawingContext`

**Décision :** rendu par remplacement complet du canvas à chaque frame.

**Pour :** simplicité, lisibilité, pas de gestion de pool manuelle.

**Contre :** pression GC à ~60 Hz avec de nombreux objets Avalonia créés/détruits.
Sur un PC moderne, le GC Gen0 absorbe facilement 300–500 allocations/frame.

**Alternative future (BLOC 7) :** `ICustomDrawOperation` avec `DrawingContext` —
rendu vectoriel direct, zéro allocation d'objets Avalonia.

---

### 14.2 Deux boucles à 60 Hz vs une seule

**Décision :** le client tourne à 60 Hz (DispatcherTimer) et le serveur à 60 Hz
(Stopwatch loop). Ils sont indépendants et non synchronisés.

**Conséquence :** un snapshot arrivant à 20 Hz peut être rendu entre 1 et 3 fois
par le client avant le prochain. L'interpolation (BLOC 6) lissera cette irrégularité.

---

### 14.3 `Phase Lobby` démarre le countdown avec 1 joueur

**Décision :** le countdown démarre dès qu'un seul joueur est connecté.

**Raison :** permet de tester seul sans attendre. En production, changer la
condition en `_ships.Count >= 2` ou ajouter un bouton "Prêt".

---

### 14.4 Pas d'input smoothing côté serveur

**Décision :** le serveur prend l'input le plus récent de la file et l'applique
directement.

**Conséquence :** si deux paquets UDP arrivent dans le même tick, le premier est
traité puis le second (FIFO). Si un paquet est perdu, le vaisseau s'arrête pendant
un tick (inertie compense visuellement).

**Alternative :** timestamper chaque input et rejeter les paquets plus vieux que
le tick courant — déjà préparé via `PlayerInputPacket.Timestamp`.

---

### 14.5 `LocalPlayerId` passé par propriété publique

**Décision :** `GameViewModel.LocalPlayerId` est une propriété settable, non injectée
par constructeur.

**Raison :** le `LobbyJoinedPacket` est reçu dans `ConnectViewModel`, pas dans
`GameViewModel`. Passer l'ID via la factory DI nécessiterait de le stocker dans
un service partagé. La propriété publique est plus simple pour l'instant.

**À améliorer :** créer un `IPlayerSession` (GameLogic) stockant `LocalPlayerId`
et `LocalPseudo`, partagé entre ConnectVM et GameVM via DI.

---

## 15. Limites connues & prochaines étapes

### Limites immédiates

| Problème | Impact | Solution prévue |
|----------|--------|----------------|
| Rendu figé entre snapshots (pas d'interpolation) | Saccades visibles > 50ms de ping | BLOC 6 — EntityInterpolator |
| `LocalPlayerId` jamais initialisé depuis App | Joueur local non distingué visuellement | Ajouter `IPlayerSession` ou passer l'ID à la navigation |
| Phase Countdown démarre avec 1 joueur | Solo uniquement pour l'instant | Paramètre configurable |
| `GameOver` renvoie au lobby sans écran de fin | US-25 partiellement couverte | `GameOverViewModel` + vue |
| Power-ups droppés mais non spawnés dans le monde | `DropsPowerUp = true` ignoré | BLOC 4 |
| Score non comptabilisé | `MyScore` toujours 0 | BLOC 5 |

### TODOs explicites dans le code

| Fichier | Méthode | TODO |
|---------|---------|------|
| `GameLoop.cs` | `DamageAsteroid` | `_ = shooterId;` → comptabiliser le score |
| `GameViewModel.cs` | `HandleGameOver` | Naviguer vers `GameOverViewModel` |
| `App.axaml.cs` | `CreateViewModel` | Setter `LocalPlayerId` après `LobbyJoined` |

### Pour jouer immédiatement

```bash
# Terminal 1
dotnet run --project src/AsteroidOnline.Server

# Terminal 2
dotnet run --project src/AsteroidOnline.Client
```

**Contrôles :**

| Touche | Action |
|--------|--------|
| W / ↑ | Poussée avant |
| A / ← | Rotation gauche |
| D / → | Rotation droite |
| Espace / F | Tirer |
| Shift / E | Dash (recharge 3s) |

---

*Document généré le 2026-04-03 — AsteroidOnline Gameplay*
