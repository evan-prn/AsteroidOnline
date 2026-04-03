# BLOC 3 — Astéroïdes : Revue technique détaillée

> **Date :** 2026-04-03
> **Branche :** `develop`
> **Build :** ✅ 0 erreur · 0 avertissement
> **User Stories couvertes :** US-13, US-14, US-15, US-16, US-17, US-18

---

## Table des matières

1. [Vue d'ensemble des changements](#1-vue-densemble-des-changements)
2. [Domain — AsteroidSize](#2-domain--asteroidsize)
3. [Domain — Asteroid](#3-domain--asteroid)
4. [Domain — AsteroidDestroyedEvent](#4-domain--asteroiddestroyedevent)
5. [Domain — CollisionSystem (partie astéroïde)](#5-domain--collisionsystem-partie-astéroïde)
6. [Server — AsteroidSpawnService](#6-server--asteroidspawnservice)
7. [Server — WaveManager](#7-server--wavemanager)
8. [Intégration dans la GameLoop](#8-intégration-dans-la-gameloop)
9. [Architecture & flux de données](#9-architecture--flux-de-données)
10. [Décisions techniques & compromis](#10-décisions-techniques--compromis)
11. [Points d'attention pour la suite](#11-points-dattention-pour-la-suite)

---

## 1. Vue d'ensemble des changements

Le BLOC 3 introduit les astéroïdes : entités physiques autonomes qui traversent la carte,
résistent aux tirs selon leur taille, se fragmentent à la destruction, et font mourir les
joueurs au contact.

**Statistiques :**

| Catégorie | Nombre |
|-----------|--------|
| Fichiers créés | 5 |
| Fichiers modifiés | 1 (GameLoop — intégration) |
| Lignes de code ajoutées | ~350 |

**Ce qui est opérationnel après ce bloc :**
- 10 astéroïdes Large spawnés au démarrage de chaque partie
- Déplacement avec rotation angulaire libre et wrap-around toroïdal
- Résistance variable selon la taille (1 à 3 PV)
- Fragmentation en cascade : Large → 2-3 Medium → 2-3 Small → rien
- Vagues automatiques toutes les 30s (+20%, plafond 80 astéroïdes)
- 20% de chance de drop de power-up à la destruction (US-17 — flag préparé)
- Mort immédiate du joueur au contact d'un astéroïde

---

## 2. Domain — AsteroidSize

### `src/AsteroidOnline.Domain/Entities/AsteroidSize.cs` (US-18)

```csharp
public enum AsteroidSize : byte
{
    Large  = 0,   // 3 PV, rayon 48 u, 60 u/s
    Medium = 1,   // 2 PV, rayon 28 u, 100 u/s
    Small  = 2,   // 1 PV, rayon 14 u, 160 u/s
}
```

**Choix du type `byte` :** lors de la sérialisation du `GameStateSnapshotPacket`, chaque
astéroïde est transmis en 21 octets. Utiliser `byte` au lieu de `int` économise 3 octets
par astéroïde ; avec 80 astéroïdes à 20 Hz, c'est 4 800 octets/s épargnés.

**Valeurs numériques explicites** (0, 1, 2) : garantissent la compatibilité binaire si
l'enum est étendu plus tard sans casser le protocole réseau existant.

---

## 3. Domain — Asteroid

### `src/AsteroidOnline.Domain/Entities/Asteroid.cs` (US-13, US-18)

Étend `PhysicalEntity` avec trois responsabilités distinctes.

**Propriétés d'état :**

| Propriété | Type | Valeur initiale | Description |
|-----------|------|-----------------|-------------|
| `Size` | `AsteroidSize` | `Large` | Taille, détermine tout le reste |
| `HitPoints` | `int` | via `GetInitialHitPoints()` | PV restants |
| `IsActive` | `bool` | `true` | `false` à la destruction |

**`CollisionRadius` — propriété calculée (US-18) :**
```csharp
public override float CollisionRadius => Size switch
{
    AsteroidSize.Large  => 48f,
    AsteroidSize.Medium => 28f,
    AsteroidSize.Small  => 14f,
    _                   => 14f,
};
```
Implémente l'abstraction de `PhysicalEntity`. Le rayon est proportionnel à la surface
visuelle de chaque taille.

**Méthodes statiques usine :**
```csharp
Asteroid.GetInitialHitPoints(AsteroidSize size)   // L=3, M=2, S=1
Asteroid.GetBaseSpeed(AsteroidSize size)           // L=60, M=100, S=160 u/s
```
Ces méthodes centralisent **tous les paramètres d'équilibrage** du jeu. Modifier la
difficulté (ex. : Large à 5 PV) se fait ici, sans toucher à `AsteroidSpawnService`
ni à la `GameLoop`.

**Tableau d'équilibrage complet (US-18) :**

| Taille | PV | Rayon | Vitesse | Surface cercle | Ratio difficulté |
|--------|-----|-------|---------|----------------|-----------------|
| Large | 3 | 48 u | 60 u/s | ~7 238 u² | Lent, grand, résistant |
| Medium | 2 | 28 u | 100 u/s | ~2 463 u² | Intermédiaire |
| Small | 1 | 14 u | 160 u/s | ~615 u² | Rapide, petit, fragile |

---

## 4. Domain — AsteroidDestroyedEvent

### `src/AsteroidOnline.Domain/Events/AsteroidDestroyedEvent.cs` (US-14, US-17)

Ce fichier définit deux types complémentaires.

### `AsteroidFragment` — DTO d'un fragment

| Propriété | Type | Description |
|-----------|------|-------------|
| `Id` | `int` | ID pré-calculé par `AsteroidSpawnService._nextId` |
| `Size` | `AsteroidSize` | Une cran en dessous du parent |
| `Position` | `Vector2` | Héritée du parent (centre de l'explosion) |
| `Velocity` | `Vector2` | Calculée avec déviation aléatoire + héritage 30% |
| `AngularVelocity` | `float` | Rotation libre propre au fragment |

### `AsteroidDestroyedEvent` — Résultat de la destruction

| Propriété | Description |
|-----------|-------------|
| `AsteroidId` | Pour retirer l'astéroïde du dictionnaire `_asteroids` |
| `Position` | Pour les effets visuels (BLOC 7) |
| `NewFragments` | Liste `IReadOnlyList<AsteroidFragment>` à instancier |
| `DropsPowerUp` | Flag 20% de chance pour Large/Medium (US-17) |

**Pourquoi un pattern Event dans Domain ?**
La logique de destruction (calcul des fragments, probabilité de drop) réside dans
`AsteroidSpawnService`. Le Domain ne sait pas comment ces fragments sont ajoutés au
monde — c'est la `GameLoop` qui lit l'événement et instancie les entités. Ce découplage
permet de tester `AsteroidSpawnService` unitairement sans `GameLoop`.

**Immutabilité** : tous les champs sont `init`-only. Une fois l'événement créé,
il ne peut plus être modifié accidentellement.

---

## 5. Domain — CollisionSystem (partie astéroïde)

### `src/AsteroidOnline.Domain/Systems/CollisionSystem.cs` (US-15)

Deux méthodes concernent les astéroïdes dans ce bloc :

**`CheckProjectileVsAsteroids(projectile, asteroids)` :**
Retourne le premier astéroïde actif touché par un projectile actif.

**`CheckAsteroidVsShip(asteroid, ships)` :**
Retourne le premier vaisseau en vie touché par un astéroïde actif.

**Optimisation — comparaison sur les carrés :**
```csharp
private static bool Overlaps(Vector2 posA, float rA, Vector2 posB, float rB)
{
    var combinedRadius = rA + rB;
    return Vector2.DistanceSquared(posA, posB) < combinedRadius * combinedRadius;
}
```
`Vector2.DistanceSquared` évite un appel à `MathF.Sqrt`, coûteux en CPU.
À 60 Hz avec 80 astéroïdes et 16 joueurs, cela représente ~1280 tests/s
pour la seule détection astéroïde↔joueur.

**Complexité :** O(A × S) pour astéroïde↔joueur, O(P × A) pour projectile↔astéroïde,
où A = nb astéroïdes, S = nb joueurs, P = nb projectiles.
Acceptable pour les volumes cibles. Un BVH ou grille spatiale serait nécessaire au-delà
de ~200 entités totales.

---

## 6. Server — AsteroidSpawnService

### `src/AsteroidOnline.Server/Services/AsteroidSpawnService.cs` (US-13, US-14, US-16, US-17)

**Gestion des IDs :**
```csharp
private int _nextId = 1000; // Plage réservée aux astéroïdes
```
Les joueurs occupent la plage `1–999`, les astéroïdes `1000+`. Évite toute collision
d'ID dans les dictionnaires `_ships` et `_asteroids` de la `GameLoop`.

---

### `SpawnInitialWave(count = 10)` — US-13

Crée 10 astéroïdes Large, chacun positionné sur un bord de la carte et orienté
vers le centre avec ±54° de déviation aléatoire.

---

### `SpawnWave(currentCount, maxAsteroids = 80)` — US-16

```csharp
var toSpawn = (int)(currentCount * 0.20f);
toSpawn = Math.Clamp(toSpawn, 1, maxAsteroids - currentCount);
```
- **Minimum 1** : même si `currentCount` est très faible, une vague spawn au moins 1 astéroïde.
- **Clamp sur le plafond** : `maxAsteroids - currentCount` empêche de dépasser 80.
- **+20%** : croissance géométrique douce — 10 → 12 → 14 → 17 → 20…

---

### `CreateDestroyedEvent(asteroid)` — Fragmentation (US-14)

Arbre de fragmentation :

```
Large   → 2 ou 3 Medium  (random.Next(2, 4))
Medium  → 2 ou 3 Small
Small   → 0 fragment      (NewFragments vide)
```

**Calcul de vélocité des fragments :**
```csharp
var angle    = random.NextDouble() * 2π          // direction aléatoire
var speed    = Asteroid.GetBaseSpeed(childSize)   // vitesse propre à la taille
var velocity = new Vector2(cos(angle), sin(angle)) * speed
             + parentVelocity * 0.3f              // héritage 30% du parent
```
L'héritage à 30% donne l'impression que les fragments "s'écartent" du point
d'explosion tout en emportant une partie de l'élan parent — fidèle au jeu original.

**Rotation angulaire des fragments :**
```csharp
AngularVelocity = (float)((_random.NextDouble() - 0.5) * 2.0)
// → valeur dans [-1.0, 1.0] rad/s
```
Chaque fragment tourne à sa propre vitesse, ce qui crée un effet visuel de débris
chaotiques naturel.

**Drop de power-up (US-17) :**
```csharp
var dropsPowerUp = asteroid.Size != AsteroidSize.Small
                && _random.NextDouble() < 0.20;
```
Le flag `DropsPowerUp` est positionné dans l'événement. La `GameLoop` décide quoi
en faire (BLOC 4 à implémenter). Le Domain reste ignorant des power-ups.

---

### `SpawnOnEdge()` — Spawn sur les bords (US-13)

```csharp
var edge = _random.Next(4);
return edge switch
{
    0 => new Vector2(random.X, 0f),              // bord haut
    1 => new Vector2(random.X, bounds.Height),   // bord bas
    2 => new Vector2(0f,       random.Y),        // bord gauche
    _ => new Vector2(bounds.Width, random.Y),    // bord droit
};
```

**Orientation vers le centre + déviation :**
```csharp
var toCenter   = centre - position;
var baseAngle  = Atan2(toCenter.Y, toCenter.X);
var deviation  = random * π * 0.6;  // ±54°
```
Sans déviation, tous les astéroïdes iraient droit au centre. Avec ±54°, ils
traversent la carte en diagonale, couvrant l'espace de jeu uniformément.

---

### `CreateFromFragment(fragment)` — Méthode statique

Convertit un `AsteroidFragment` (DTO d'événement, couche Domain) en `Asteroid`
(entité du monde, couche Server). Séparation nette entre la description de ce qui
doit se passer (événement) et son instanciation (factory côté serveur).

---

## 7. Server — WaveManager

### `src/AsteroidOnline.Server/Services/WaveManager.cs` (US-16)

```csharp
public bool Tick(float deltaTime, int currentAsteroidCount)
{
    if (currentAsteroidCount >= MaxAsteroids) return false;
    _waveTimer += deltaTime;
    if (_waveTimer < WaveInterval) return false;
    _waveTimer -= WaveInterval;   // ← et non pas = 0f
    CurrentWave++;
    return true;
}
```

**Constantes :**

| Constante | Valeur | Description |
|-----------|--------|-------------|
| `WaveInterval` | 30s | Intervalle entre deux vagues |
| `MaxAsteroids` | 80 | Plafond (US-16) |

**Pourquoi `_waveTimer -= WaveInterval` et non `= 0f` ?**
Si un tick est légèrement en retard (ex. 31s au lieu de 30s), `_waveTimer` vaudra
`1s` après la soustraction. La prochaine vague arrivera dans 29s au lieu de 30s,
corrigeant naturellement la dérive. Avec `= 0f`, la dérive s'accumule sur la durée
de la partie.

**`SecondsUntilNextWave`** : propriété calculée exposée pour un futur affichage HUD.

---

## 8. Intégration dans la GameLoop

### Cycle de vie d'un astéroïde dans `GameLoop.cs`

```
[StartGame]
    AsteroidSpawnService.SpawnInitialWave(10)
    → _asteroids[id] = asteroid (×10 Large)

[TickPlaying — chaque frame 60 Hz]
    PhysicsSystem.Tick(asteroid, dt, bounds)
    → position += velocity × dt
    → velocity *= 0.99 (drag)
    → rotation += angularVelocity × dt
    → WrapAround(position)

[ProcessCollisions]
    CheckProjectileVsAsteroids(proj, _asteroids.Values)
    → hit != null → DamageAsteroid(hit, proj.OwnerId)
        asteroid.HitPoints--
        if HitPoints == 0:
            asteroid.IsActive = false
            _asteroids.Remove(id)
            evt = AsteroidSpawnService.CreateDestroyedEvent(asteroid)
            foreach fragment in evt.NewFragments:
                _asteroids[fragment.Id] = CreateFromFragment(fragment)

    CheckAsteroidVsShip(asteroid, _ships.Values)
    → victim != null → EliminatePlayer(victim, killerId=-1)

[WaveManager.Tick(dt, _asteroids.Count)]
    → returns true toutes les 30s si count < 80
    → AsteroidSpawnService.SpawnWave() → nouveaux Large ajoutés
```

**Fragmentation en cascade automatique :** les nouveaux Medium et Small sont ajoutés
à `_asteroids` dans le même tick. Ils seront touchables dès le tick suivant. La `GameLoop`
n'a pas besoin de logique spéciale : la récursivité émerge naturellement de la structure.

---

## 9. Architecture & flux de données

### Flux de destruction d'un astéroïde Large

```
[Projectile atteint Asteroid Large (PV=3)]
        │
        ▼ ProcessCollisions()
DamageAsteroid(asteroid, shooterId)
    asteroid.HitPoints = 2   → rien (PV restants)

[Second projectile]
    asteroid.HitPoints = 1   → rien

[Troisième projectile]
    asteroid.HitPoints = 0
        │
        ▼
    asteroid.IsActive = false
    _asteroids.Remove(asteroid.Id)
        │
        ▼
    CreateDestroyedEvent(asteroid)
    → NewFragments = [ Medium₁, Medium₂, Medium₃ ]
    → DropsPowerUp = (random < 0.20)
        │
        ▼
    foreach fragment → CreateFromFragment(fragment)
    → _asteroids[Medium₁.Id] = Medium₁
    → _asteroids[Medium₂.Id] = Medium₂
    → _asteroids[Medium₃.Id] = Medium₃

[Tick suivant]
    PhysicsSystem.Tick(Medium₁, ...)   ← déjà dans la boucle
    ...
    [Si un projectile touche Medium₁]
    DamageAsteroid(Medium₁)
    → HitPoints = 1 → 0 → fragments Small × 2-3
```

---

### Graphe de dépendances (BLOC 3)

```
Domain: AsteroidSize, Asteroid (← PhysicalEntity)
        AsteroidDestroyedEvent, AsteroidFragment
        CollisionSystem (méthodes astéroïde)
    ▲
Shared: (pas de nouveaux paquets dans ce bloc)

Server: AsteroidSpawnService (← Domain)
        WaveManager
        GameLoop (intégration)
```

---

## 10. Décisions techniques & compromis

### 10.1 Pas d'entité `PowerUp` dans ce bloc

**Décision :** `DropsPowerUp` est un flag dans l'événement, pas une entité instanciée.

**Raison :** le BLOC 4 est dédié aux power-ups. Instancier une entité `PowerUp` ici
créerait une dépendance précoce. Le flag prépare le terrain sans lier les deux blocs.

**Impact :** côté `GameLoop`, `evt.DropsPowerUp` est ignoré pour l'instant avec un
commentaire `// TODO BLOC 4`.

---

### 10.2 Fragmentation dans `AsteroidSpawnService` et non dans `CollisionSystem`

**Décision :** la logique de fragmentation vit dans `AsteroidSpawnService`, pas dans
`CollisionSystem`.

**Raison :** `CollisionSystem` détecte les collisions et retourne les entités touchées.
Il n'a pas à connaître la logique métier de fragmentation — ce serait une violation de
responsabilité unique. `AsteroidSpawnService` gère le cycle de vie complet des astéroïdes
(création, fragmentation).

---

### 10.3 Vitesse des Small : 160 u/s vs MaxSpeed joueur : 400 u/s

Les petits astéroïdes (160 u/s) sont deux fois plus lents que le vaisseau à pleine
vitesse (400 u/s). Le joueur peut donc les éviter facilement à haute vitesse, mais
ils sont difficiles à toucher à cause de leur petit rayon (14 u).

**Compromis volontaire** : les Small sont la menace secondaire (difficiles à toucher
en tir normal) et non la menace principale (les Large, lents mais résistants).

---

### 10.4 Rayon de collision vs rayon visuel

Le rayon de collision est légèrement inférieur au rayon visuel réel du polygone rendu :
- Large : collision 48u, visuel ~50-52u (octogone irrégulier)
- Medium : collision 28u, visuel ~30u

Ce "hitbox forgiveness" est une pratique courante dans les jeux d'arcade : le joueur
perçoit les collisions comme justes même si elles sont légèrement plus permissives
que l'apparence.

---

## 11. Points d'attention pour la suite

### TODOs dans le code

| Fichier | Emplacement | TODO |
|---------|------------|------|
| `GameLoop.cs` | `DamageAsteroid` | `evt.DropsPowerUp` ignoré → BLOC 4 |
| `GameLoop.cs` | `DamageAsteroid` | `_ = shooterId` → comptabiliser le score (BLOC 5) |

### Pour BLOC 4 (Power-ups)

- Créer `Domain/Entities/PowerUp.cs` (type, durée de vie, position)
- Dans `GameLoop.DamageAsteroid` : si `evt.DropsPowerUp`, instancier un `PowerUp`
- `PhysicsSystem` n'a pas besoin d'être modifié (les power-ups sont statiques)
- Durée de vie 8s → timer dans la GameLoop

### Pour BLOC 7 (Rendu)

- Les astéroïdes sont actuellement rendus comme des octogones dans `GameRenderer`
- Ajouter des effets de particules à la destruction (`AsteroidDestroyedEvent.Position`)
- Afficher les PV restants sur les Large (barre de vie ou couleur)

---

*Document généré le 2026-04-03 — AsteroidOnline BLOC 3*
