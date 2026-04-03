# BLOC 5 — Battle Royale & Combats : Revue technique détaillée

> **Date :** 2026-04-03
> **Branche :** `develop`
> **Build :** ✅ 0 erreur · 0 avertissement
> **User Stories couvertes :** US-22, US-23, US-24, US-25, US-27

---

## Table des matières

1. [Vue d'ensemble des changements](#1-vue-densemble-des-changements)
2. [Domain — PlayerKilledEvent](#2-domain--playerkilledevent)
3. [Domain — CollisionSystem (PvP)](#3-domain--collisionsystem-pvp)
4. [Shared — GameStateSnapshotPacket](#4-shared--gamestatesnapshotpacket)
5. [Shared — PlayerEliminatedPacket](#5-shared--playereliminatedpacket)
6. [Shared — GameOverPacket](#6-shared--gameoverpacket)
7. [Server — GameLoop autoritaire](#7-server--gameloop-autoritaire)
8. [Server — Program.cs](#8-server--programcs)
9. [Client — Intégration dans GameViewModel](#9-client--intégration-dans-gameviewmodel)
10. [Architecture & flux complet d'une partie](#10-architecture--flux-complet-dune-partie)
11. [Décisions techniques & compromis](#11-décisions-techniques--compromis)
12. [Points d'attention pour la suite](#12-points-dattention-pour-la-suite)

---

## 1. Vue d'ensemble des changements

Le BLOC 5 rend la partie **multijoueur** : les joueurs peuvent s'entre-tuer, les
éliminations sont diffusées à tous, et la partie se termine quand un seul joueur survit.
C'est aussi le bloc qui intègre le **serveur autoritaire 60 Hz** et le **système de
snapshot UDP à 20 Hz**.

**Statistiques :**

| Catégorie | Nombre |
|-----------|--------|
| Fichiers créés | 5 |
| Fichiers modifiés | 2 (GameLoop — logique PvP, GameViewModel — réception) |
| Lignes de code ajoutées | ~500 |

**Ce qui est opérationnel après ce bloc :**
- Détection de collision projectile↔joueur (propriétaire exclu)
- Élimination immédiate au contact (sans bouclier — BLOC 4)
- Diffusion TCP de chaque élimination à tous les clients
- Snapshot UDP 20 Hz de l'état complet (joueurs + astéroïdes + projectiles)
- Feed d'éliminations HUD affiché 4 secondes (US-24)
- Détection de fin de partie et notification du vainqueur (US-25)
- Serveur autoritaire : les clients n'envoient que des intentions, jamais des positions

---

## 2. Domain — PlayerKilledEvent

### `src/AsteroidOnline.Domain/Events/PlayerKilledEvent.cs` (US-15, US-22)

```csharp
public sealed class PlayerKilledEvent
{
    public int    VictimId   { get; init; }
    public string VictimName { get; init; } = string.Empty;
    public int    KillerId   { get; init; } = -1;    // -1 = astéroïde
    public string KillerName { get; init; } = "Astéroïde";
}
```

**Convention `KillerId = -1` :** sentinel value indiquant une mort par astéroïde.
Utilisée dans `GameLoop.EliminatePlayer(victim, killerId)` pour distinguer les
kills PvP des morts environnementales sans créer deux méthodes différentes.

**Immuabilité (`init`)** : une fois créé, l'événement ne peut plus être modifié.
Garantit la cohérence si l'objet est transmis à plusieurs consommateurs.

Ce type Domain sert de base pour les tests unitaires futurs — on peut vérifier
les conditions d'élimination sans instancier la `GameLoop`.

---

## 3. Domain — CollisionSystem (PvP)

### `src/AsteroidOnline.Domain/Systems/CollisionSystem.cs` (US-22)

La méthode PvP du système de collision :

```csharp
public Ship? CheckProjectileVsShips(Projectile projectile, IEnumerable<Ship> ships)
{
    if (!projectile.IsActive) return null;

    foreach (var ship in ships)
    {
        if (ship.Id == projectile.OwnerId || !ship.IsAlive) continue;

        if (Overlaps(projectile.Position, projectile.CollisionRadius,
                     ship.Position,       ship.CollisionRadius))
            return ship;
    }
    return null;
}
```

**Deux gardes critiques :**

1. `ship.Id == projectile.OwnerId` : un joueur **ne peut pas se tuer avec ses propres projectiles**. Sans cette garde, tirer vers l'arrière en reculant provoquerait une mort immédiate.

2. `!ship.IsAlive` : un joueur déjà mort n'est pas retesté. Son vaisseau reste dans
   `_ships` pour continuer à apparaître dans les snapshots (animation de mort côté client),
   mais ne peut plus être touché.

**Optimisation `DistanceSquared` :**
```csharp
private static bool Overlaps(Vector2 posA, float rA, Vector2 posB, float rB)
{
    var d = rA + rB;
    return Vector2.DistanceSquared(posA, posB) < d * d;
}
```
Évite le `MathF.Sqrt` coûteux. À 60 Hz avec 16 joueurs et 50 projectiles actifs :
`50 × 16 = 800 tests/tick × 60 = 48 000 tests/s`. L'optimisation est mesurable.

---

## 4. Shared — GameStateSnapshotPacket

### `src/AsteroidOnline.Shared/Packets/GameStateSnapshotPacket.cs` (US-23)

Paquet UDP le plus volumineux du protocole. Diffusé à **20 Hz** (toutes les 3 ticks à 60 Hz).

**Trois types de snapshot imbriqués :**

#### `PlayerSnapshot` (26 octets)

| Champ | Octets | Description |
|-------|--------|-------------|
| `Id` | 4 | Identifiant |
| `X`, `Y` | 4+4 | Position |
| `Rotation` | 4 | Angle (rad) |
| `VelocityX`, `VelocityY` | 4+4 | Pour l'extrapolation client (BLOC 6) |
| `Color` | 1 | `PlayerColor` (byte) |
| `IsAlive` | 1 | Mort ou vivant |
| `DashCooldownProgress` | 4 | Corrige la prédiction locale |
| **Total** | **26** | |

#### `AsteroidSnapshot` (21 octets)

| Champ | Octets |
|-------|--------|
| `Id` | 4 |
| `X`, `Y` | 8 |
| `Rotation` | 4 |
| `Size` | 1 (byte) |
| `HitPoints` | 4 |
| **Total** | **21** |

#### `ProjectileSnapshot` (16 octets)

| Champ | Octets |
|-------|--------|
| `Id`, `X`, `Y`, `OwnerId` | 4+4+4+4 |
| **Total** | **16** |

**Estimation de bande passante (pire cas) :**
```
En-tête (timestamp + aliveCount) :  12 octets
16 joueurs    × 26 octets        : 416 octets
80 astéroïdes × 21 octets        : 1 680 octets
50 projectiles × 16 octets       : 800 octets
                            Total : ~2 908 octets ≈ 2.8 Ko/snapshot

À 20 Hz → 16 clients : 2.9 Ko × 20 × 16 ≈ 928 Ko/s en sortie serveur
```
Raisonnable pour du LAN ou une connexion fibre.

**Sérialisation ordre des champs :** l'ordre est fixe et documenté dans le code.
Toute modification de l'ordre ou ajout d'un champ doit incrémenter la version du
protocole (préfixée dans la clé de connexion `"AsteroidOnline_v1"`).

---

## 5. Shared — PlayerEliminatedPacket

### `src/AsteroidOnline.Shared/Packets/PlayerEliminatedPacket.cs` (US-24)

```csharp
public class PlayerEliminatedPacket : IPacket
{
    public PacketType Type => PacketType.PlayerEliminated;
    public int    VictimId   { get; set; }
    public string VictimName { get; set; } = string.Empty;
    public string KillerName { get; set; } = string.Empty;
}
```

**Canal de livraison : `ReliableOrdered` (TCP-like).**
Une élimination est un événement critique : si ce paquet est perdu, le feed
HUD n'est pas mis à jour et le score est faux. Le coût de la garantie de livraison
(~1 retransmission en cas de perte) est négligeable pour un événement aussi rare.

**Taille typique :** ~30 octets (IDs + 2 strings UTF-8 courtes).

**`VictimId`** : en plus des noms, l'ID permet au client de masquer immédiatement
le vaisseau du joueur éliminé sans attendre le prochain snapshot.

---

## 6. Shared — GameOverPacket

### `src/AsteroidOnline.Shared/Packets/GameOverPacket.cs` (US-25)

```csharp
public class GameOverPacket : IPacket
{
    public PacketType Type => PacketType.GameOver;
    public int    WinnerId   { get; set; }
    public string WinnerName { get; set; } = string.Empty;
}
```

**Minimal par design :** envoyé une seule fois en `ReliableOrdered`. L'écran de
fin de partie n'a besoin que du nom du vainqueur pour l'afficher.

`WinnerId = -1` + `WinnerName = "Personne"` gère le cas edge où tous les joueurs
meurent simultanément (collision croisée dans le même tick).

---

## 7. Server — GameLoop autoritaire

### `src/AsteroidOnline.Server/GameLoop.cs` (US-22, US-23, US-24, US-25, US-27)

Fichier central du serveur. Voici une analyse des parties propres au BLOC 5.

---

### 7.1 Machine à états de session

```
[Lobby]
  condition : ≥ 1 joueur connecté
  ▼
[Countdown]  BroadcastCountdown chaque seconde (5, 4, 3, 2, 1, 0)
  condition : SecondsRemaining == 0
  ▼
[Playing]    simulation 60 Hz
  condition : aliveCount <= 1
  ▼
[GameOver]   BroadcastGameOver → arrêt
```

**Pourquoi démarre avec 1 joueur ?** Pour permettre les tests solo sans avoir
à lancer deux clients. Changer la condition en `>= 2` pour la production.

---

### 7.2 Boucle principale 60 Hz (US-27)

```csharp
var nextTick = sw.Elapsed.TotalMilliseconds;

while (!ct.IsCancellationRequested)
{
    var now = sw.Elapsed.TotalMilliseconds;
    if (now < nextTick)
    {
        if ((int)(nextTick - now) > 1)
            await Task.Delay(1, ct);
        continue;
    }
    nextTick += TickDurationMs;   // accumulation, pas reset
    _netManager.PollEvents();
    Tick(deltaTime);
}
```

**`nextTick += dt` vs `nextTick = now + dt` :**
L'accumulation compense automatiquement les ticks légèrement en retard.
Avec `= now + dt`, un spike de 2ms se traduit par 2ms de retard permanent.
Avec `+= dt`, le retard est résorbé au tick suivant (plus rapide).

**`Task.Delay(1)` vs `Thread.Sleep(1)` :**
`Task.Delay` libère le thread pool entre les ticks, permettant au GC de s'exécuter.
`Thread.Sleep` bloquerait un thread OS pendant toute la durée de la partie.

**Autorité du serveur (US-27) :**
Les clients n'envoient que des **intentions** (`ThrustForward`, `RotateLeft`, `Fire`…),
jamais des positions. Le serveur recalcule toujours la physique et les collisions de
son côté. Un client ne peut pas tricher en envoyant une position falsifiée.

---

### 7.3 File d'inputs thread-safe

```csharp
private readonly ConcurrentQueue<(int PlayerId, PlayerInputPacket Input)> _inputQueue;
```

Les callbacks LiteNetLib (`OnNetworkReceive`) s'exécutent sur le thread réseau.
La boucle de jeu tourne sur le thread `async` de `RunAsync`. La `ConcurrentQueue`
est le seul pont thread-safe sans verrou explicite.

**`ProcessInputQueue()` — traitement dans `TickPlaying` :**
```csharp
while (_inputQueue.TryDequeue(out var item))
{
    _dash.Tick(ship, input.Dash, dt);
    _physics.Tick(ship, input.ThrustForward, input.RotateLeft,
                  input.RotateRight, dt, bounds);
    var proj = _weapon.TryFire(ship, input.Fire, _nextProjectileId);
    if (proj != null) _projectiles[proj.Id] = proj;
}
```

---

### 7.4 `EliminatePlayer(victim, killerId)` — US-22, US-24

```csharp
private void EliminatePlayer(Ship victim, int killerId)
{
    if (!victim.IsAlive) return;   // garde anti-double-kill

    victim.IsAlive = false;
    _ships.TryGetValue(killerId, out var killer);

    BroadcastReliable(new PlayerEliminatedPacket
    {
        VictimId   = victim.Id,
        VictimName = victim.Pseudo,
        KillerName = killer?.Pseudo ?? "Astéroïde",
    });
}
```

**Garde `if (!victim.IsAlive) return`** : critique. Sans elle, si deux projectiles
touchent le même joueur dans le même tick, deux `PlayerEliminatedPacket` seraient
envoyés et le score du tueur serait doublé.

**`killer?.Pseudo ?? "Astéroïde"` :** couvre les deux cas (US-22 = kill PvP,
US-15 = mort par astéroïde avec `killerId = -1`).

---

### 7.5 `BroadcastSnapshot()` — US-23 (20 Hz)

```csharp
_snapshotAccumulator += dt;
if (_snapshotAccumulator >= (TickDurationMs * SnapshotEveryNTicks / 1000f))
{
    _snapshotAccumulator = 0f;
    BroadcastSnapshot();
}
```

`SnapshotEveryNTicks = 3` → 60 Hz / 3 = **20 Hz** de snapshots.

**Pourquoi 20 Hz et non 60 Hz ?**
Un snapshot complet pèse ~2.8 Ko. À 60 Hz × 16 clients, cela ferait ~2.7 Mo/s
en sortie — trop pour une connexion résidentielle. À 20 Hz : ~0.9 Mo/s, acceptable.
La fluidité visuelle est assurée par l'interpolation côté client (BLOC 6).

**`DashCooldownProgress` dans le snapshot :**
Permet au client de corriger sa prédiction locale sans paquet dédié. Si la prédiction
et le serveur divergent, le snapshot remet tout à plat silencieusement.

---

### 7.6 `CheckGameOver()` — US-25

```csharp
private void CheckGameOver()
{
    var alive = _ships.Values.Count(s => s.IsAlive);
    if (alive > 1 || _ships.Count <= 1) return;

    var winner = _ships.Values.FirstOrDefault(s => s.IsAlive);
    _phase = GamePhase.GameOver;

    BroadcastReliable(new GameOverPacket
    {
        WinnerId   = winner?.Id ?? -1,
        WinnerName = winner?.Pseudo ?? "Personne",
    });
}
```

**`_ships.Count <= 1` :** si un seul joueur est connecté (tests solo), la partie
ne se termine pas immédiatement après sa propre mort — ce serait absurde.

**`winner == null`** (tous morts simultanément) : géré par `?? -1` / `?? "Personne"`.
Cas rare mais possible si deux joueurs se tuent mutuellement dans le même tick.

---

### 7.7 Pipeline complet `TickPlaying`

```
1. ProcessInputQueue()          ← inputs clients → physique + tirs
2. PhysicsSystem.Tick(ships)    ← inertie des vaisseaux (sans input)
3. PhysicsSystem.Tick(asteroids)
4. Projectiles : lifetime -= dt + Tick() + suppression expirés
5. ProcessCollisions()
   ├── proj ↔ astéroïdes  → DamageAsteroid()
   ├── proj ↔ joueurs     → EliminatePlayer()
   └── astéroïdes ↔ joueurs → EliminatePlayer()
6. WaveManager.Tick()           → SpawnWave() si nécessaire
7. Snapshot toutes les 3 ticks  → BroadcastSnapshot()
8. CheckGameOver()
```

**Ordre des étapes 5a et 5b :** les collisions proj↔astéroïdes sont traitées
**avant** proj↔joueurs. Un projectile qui touche un astéroïde est désactivé
(`IsActive = false`, retiré de `_projectiles`) et ne peut plus toucher un joueur
dans le même tick. Comportement voulu : un joueur "derrière" un astéroïde est protégé.

---

## 8. Server — Program.cs

```csharp
using var loggerFactory = LoggerFactory.Create(b =>
    b.AddConsole().SetMinimumLevel(LogLevel.Information));

using var gameLoop = new GameLoop(loggerFactory.CreateLogger<GameLoop>());
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
await gameLoop.RunAsync(cts.Token);
```

**Niveaux de log utilisés dans `GameLoop` :**

| Niveau | Événements |
|--------|-----------|
| `Information` | Connexion/déconnexion joueur, démarrage partie, vagues, fin de partie |
| `Debug` | Destruction d'astéroïde (trop fréquent pour `Information`) |
| `Warning` | Erreurs réseau, paquets malformés |

**`ILogger<GameLoop>` (Microsoft.Extensions.Logging) :** conforme aux exigences
du prompt maître. Permet de brancher n'importe quel provider de log (fichier,
Seq, OpenTelemetry) sans modifier le code de `GameLoop`.

**Ajout au `Server.csproj` :**
```xml
<PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="10.0.0" />
```

---

## 9. Client — Intégration dans GameViewModel

### Réception du snapshot (US-23)

```csharp
private void HandleSnapshot(BinaryReader reader)
{
    var packet = new GameStateSnapshotPacket();
    packet.Deserialize(reader);

    Dispatcher.UIThread.Post(() =>
    {
        _lastSnapshot     = packet;
        AlivePlayersCount = packet.AlivePlayersCount;

        // Correction de la prédiction locale du dash
        var mySnap = packet.Players.Find(p => p.Id == LocalPlayerId);
        if (mySnap is not null)
            _dashCooldownRemaining = (1f - mySnap.DashCooldownProgress)
                                   * DashSystem.CooldownDuration;
    });
}
```

**`Dispatcher.UIThread.Post`** : la désérialisation se fait sur le thread de
polling (appelé depuis `LiteNetClientService`), mais la mise à jour des propriétés
observables doit se faire sur le UI thread Avalonia. `Post` est non-bloquant : le
thread de polling ne s'arrête pas en attendant la mise à jour UI.

**Réconciliation du dash** : `DashCooldownProgress` dans le snapshot sert à corriger
la prédiction locale. Si la prédiction et le serveur divergent de plus de quelques
centièmes de seconde (perte de paquet UDP), la prochaine correction silencieuse
évite toute saccade visible.

---

### Feed d'éliminations (US-24)

```csharp
private void HandlePlayerEliminated(BinaryReader reader)
{
    var packet = new PlayerEliminatedPacket();
    packet.Deserialize(reader);

    Dispatcher.UIThread.Post(() =>
    {
        EliminationFeedText = $"{packet.KillerName} a éliminé {packet.VictimName}";
        ShowEliminationFeed = true;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (s, _) =>
        {
            ShowEliminationFeed = false;
            ((DispatcherTimer)s!).Stop();
        };
        timer.Start();
    });
}
```

**`DispatcherTimer` one-shot :** pattern classique pour un masquage automatique
après un délai sans bloquer le thread UI. Le timer se détruit lui-même en s'arrêtant.

**Limite :** si plusieurs joueurs sont éliminés rapidement (ex. explosion en chaîne),
seul le dernier message est affiché — le précédent est écrasé. Un vrai feed utiliserait
une `ObservableCollection<string>` avec défilement. Acceptable pour le prototype.

---

### Fin de partie (US-25)

```csharp
private void HandleGameOver(BinaryReader reader)
{
    var packet = new GameOverPacket();
    packet.Deserialize(reader);

    Dispatcher.UIThread.Post(() =>
    {
        _networkService.PacketReceived -= OnPacketReceived;
        // TODO : naviguer vers GameOverViewModel (affichage vainqueur + bouton rejouer)
        _navigationService.NavigateTo<LobbyViewModel>();
    });
}
```

Le désabonnement de `PacketReceived` avant la navigation évite que des paquets
résiduels (snapshots encore en transit) soient traités après la navigation.

---

## 10. Architecture & flux complet d'une partie

### Flux d'un kill PvP (US-22)

```
[Joueur A appuie sur Espace]
        │
        ▼ UDP → Serveur
PlayerInputPacket { Fire = true }
        │
        ▼ ProcessInputQueue()
WeaponSystem.TryFire(shipA, fire=true, nextId)
    → Projectile { OwnerId=A.Id } ajouté à _projectiles
        │
        ▼ [1-10 ticks plus tard] ProcessCollisions()
CollisionSystem.CheckProjectileVsShips(proj, _ships.Values)
    → Overlaps(proj.Position, 4u, shipB.Position, 16u) → true
        │
        ▼ EliminatePlayer(shipB, killerId=A.Id)
    shipB.IsAlive = false
    BroadcastReliable(PlayerEliminatedPacket {
        VictimId=B, VictimName="Bob", KillerName="Alice"
    })
        │
        ▼ TCP → Tous les clients
HandlePlayerEliminated()
    → EliminationFeedText = "Alice a éliminé Bob"
    → ShowEliminationFeed = true  (masqué après 4s)
        │
        ▼ [Prochain snapshot 20Hz]
BroadcastSnapshot()
    → PlayerSnapshot { Id=B, IsAlive=false }
        │
        ▼ GameRenderer.DrawShip(shipB)
    → if (!ship.IsAlive) return  ← vaisseau de Bob invisible
```

---

### Flux de fin de partie (US-25)

```
[Seul Alice survit]
        │
        ▼ CheckGameOver() — appelé chaque tick
alive = _ships.Values.Count(s => s.IsAlive)  // = 1
_phase = GamePhase.GameOver
BroadcastReliable(GameOverPacket { WinnerId=Alice.Id, WinnerName="Alice" })
        │
        ▼ TCP → Tous les clients
HandleGameOver()
    PacketReceived -= OnPacketReceived   (désabonnement propre)
    NavigateTo<LobbyViewModel>()         (TODO : → GameOverViewModel)
```

---

## 11. Décisions techniques & compromis

### 11.1 Snapshot UDP vs événements TCP pour les positions

**Décision :** les positions sont transmises en UDP (`GameStateSnapshotPacket`),
les événements critiques en TCP (`PlayerEliminatedPacket`, `GameOverPacket`).

**Raison :** les positions sont envoyées 20 fois par seconde ; une perte occasionnelle
est compensée par le snapshot suivant 50ms plus tard. Un kill ou une fin de partie
perdu serait catastrophique pour l'expérience — la garantie de livraison est obligatoire.

---

### 11.2 Vaisseaux morts conservés dans `_ships`

**Décision :** `EliminatePlayer` met `IsAlive = false` mais ne retire pas le vaisseau
de `_ships`.

**Raison :** le vaisseau continue d'apparaître dans les snapshots avec `IsAlive = false`.
Le client peut afficher une animation de mort ou un indicateur "éliminé".
Si on retirait le vaisseau de `_ships`, les clients verraient le vaisseau disparaître
instantanément sans transition.

---

### 11.3 Pas de reconnexion (US-28 — Nice to have)

Un joueur déconnecté (`OnPeerDisconnected`) est retiré de `_peers` et son vaisseau
est mis mort. La reconnexion (US-28) nécessiterait de conserver la session pendant
un délai (ex. 30s) et de réassigner le même `PlayerId` au pair reconnecté.
Non implémentée dans ce bloc.

---

### 11.4 `_snapshotAccumulator` vs compteur de ticks

**Décision :** accumulation en secondes plutôt que compteur de ticks.

```csharp
// Approche retenue (accumulation temps réel)
_snapshotAccumulator += dt;
if (_snapshotAccumulator >= targetInterval) { ... }

// Approche alternative (compteur de ticks)
if (_tickCount % 3 == 0) BroadcastSnapshot();
```

L'accumulation en temps réel est plus robuste si le deltaTime varie légèrement.
Avec un compteur, un tick raté (spike CPU) décale tous les snapshots suivants.

---

## 12. Points d'attention pour la suite

### TODOs dans le code

| Fichier | Méthode | TODO |
|---------|---------|------|
| `GameViewModel.cs` | `HandleGameOver` | Naviguer vers `GameOverViewModel` (US-25) |
| `GameLoop.cs` | `DamageAsteroid` | Comptabiliser le score du tireur (US-29) |
| `GameLoop.cs` | `TickLobby` | `>= 2` joueurs pour la production |

### Pour un GameOverViewModel (US-25 complet)

- Créer `Client/ViewModels/GameOverViewModel.cs` avec `WinnerName` + commande "Rejouer"
- La commande "Rejouer" navigue vers `ConnectViewModel` (ou directement `LobbyViewModel`
  si la connexion est toujours active)
- Vue animée `GameOverView.axaml` avec transition CSS/Avalonia

### Pour le score (US-29)

Actuellement `MyScore` est toujours 0. Il faut :
1. Ajouter `Score` à `Ship` (côté serveur)
2. L'incrémenter dans `DamageAsteroid` (points astéroïdes) et `EliminatePlayer` (kill)
3. L'inclure dans `PlayerSnapshot` (1 champ `int` supplémentaire)
4. Lire `mySnap.Score` dans `HandleSnapshot()` → `MyScore`

### Pour l'interpolation (BLOC 6 — US-26)

Sans interpolation, le rendu est "saccadé" entre deux snapshots (50ms d'écart).
`EntityInterpolator` doit maintenir un buffer de snapshots horodatés et interpoler
linéairement entre t-1 et t (avec 100ms de délai de rendu).

---

*Document généré le 2026-04-03 — AsteroidOnline BLOC 5*
