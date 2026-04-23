# AsteroidOnline — Compte rendu technique complet

> Document de référence décrivant l'architecture, les choix techniques et les motivations derrière chaque décision du projet.

---

## Table des matières

1. [Présentation du projet](#1-présentation-du-projet)
2. [Architecture globale](#2-architecture-globale)
3. [Couche Domain](#3-couche-domain)
4. [Couche Shared — Protocole réseau](#4-couche-shared--protocole-réseau)
5. [Couche GameLogic — Interfaces applicatives](#5-couche-gamelogic--interfaces-applicatives)
6. [Couche Infrastructure — Réseau client](#6-couche-infrastructure--réseau-client)
7. [Serveur autoritaire](#7-serveur-autoritaire)
8. [Client Avalonia UI](#8-client-avalonia-ui)
9. [Rendu 2D](#9-rendu-2d)
10. [Flux réseau de bout en bout](#10-flux-réseau-de-bout-en-bout)
11. [Choix techniques et justifications](#11-choix-techniques-et-justifications)
12. [Constantes et paramètres de jeu](#12-constantes-et-paramètres-de-jeu)
13. [Déploiement](#13-déploiement)
14. [Limites connues et pistes d'évolution](#14-limites-connues-et-pistes-dévolution)

---

## 1. Présentation du projet

**AsteroidOnline** est un jeu d'arcade spatial multijoueur inspiré du classique _Asteroids_ (Atari, 1979). Il adapte la boucle de jeu inertielle originale à un contexte réseau moderne, avec jusqu'à 20 joueurs simultanés évoluant dans la même arène.

### Objectifs

- Reproduire la physique inertielle caractéristique d'Asteroids (poussée, rotation, wrap-around toroïdal).
- Rendre la partie jouable aussi bien en solo qu'en multijoueur sans changer la logique.
- Maintenir un état de jeu autoritaire côté serveur pour prévenir toute désynchronisation ou triche.
- Garder une base de code propre, découplée et maintenable dans le temps.

### Stack technique

| Composant | Technologie | Version |
|---|---|---|
| Langage | C# | 12 |
| Runtime | .NET | 10.0 |
| UI client | AvaloniaUI | 11.3.12 |
| Paradigme UI | MVVM (CommunityToolkit.Mvvm) | 8.2.1 |
| Réseau | LiteNetLib | 2.1.2 |
| Audio (Windows) | WinMM P/Invoke | OS natif |
| Format solution | `.slnx` | Format moderne .NET |

---

## 2. Architecture globale

Le projet est structuré en **six assemblies distincts** organisés selon les principes de la _Clean Architecture_. Les dépendances ne vont que vers les couches inférieures ; aucune couche de bas niveau ne connaît les couches supérieures.

```
┌──────────────────────────────────────────────────────────────┐
│                  AsteroidOnline.Client                       │
│           (AvaloniaUI, MVVM, rendu, audio, inputs)           │
└──────────────────────────┬───────────────────────────────────┘
                           │ référence
┌──────────────────────────▼───────────────────────────────────┐
│               AsteroidOnline.Infrastructure                  │
│           (LiteNetClientService — implémentation UDP)        │
└──────────────────────────┬───────────────────────────────────┘
                           │ référence
┌──────────────────────────▼───────────────────────────────────┐
│                AsteroidOnline.GameLogic                      │
│        (INetworkClientService, INavigationService)           │
└──────────────────────────┬───────────────────────────────────┘
                           │ référence
┌──────────────────────────▼───────────────────────────────────┐
│                 AsteroidOnline.Shared                        │
│          (Packets, PacketType, sérialisation binaire)        │
└──────────────────────────┬───────────────────────────────────┘
                           │ référence
┌──────────────────────────▼───────────────────────────────────┐
│                 AsteroidOnline.Domain                        │
│    (Entités, Systèmes physique/collision/arme/dash, Events)  │
└──────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────┐
│                 AsteroidOnline.Server                        │
│   (GameLoop, SpawnService, AsteroidSpawnService, WaveManager)│
│   ← référence Infrastructure                                 │
└──────────────────────────────────────────────────────────────┘
```

### Pourquoi cette organisation ?

- **Domain sans dépendance** : les entités et systèmes de jeu ne savent rien du réseau, de l'UI ou du framework. Ils peuvent être testés unitairement et réutilisés dans n'importe quel contexte.
- **Shared partagé entre Server et Client** : les deux extrémités du réseau utilisent exactement les mêmes structures de paquets, évitant toute désynchronisation de protocole.
- **Infrastructure derrière une interface** : le client ne dépend jamais de LiteNetLib directement ; il passe par `INetworkClientService`. Cela facilite les tests et rend un éventuel changement de bibliothèque réseau transparent.

---

## 3. Couche Domain

`AsteroidOnline.Domain` est le cœur métier du jeu. Il ne référence aucun projet externe, aucun package NuGet lié au réseau ou à l'UI.

### 3.1 Hiérarchie des entités

Toutes les entités physiques héritent de `PhysicalEntity` :

```
PhysicalEntity (abstraite)
├── Ship          — vaisseau joueur
├── Asteroid      — astéroïde fragmentable
└── Projectile    — tir joueur
```

**`PhysicalEntity`** expose :
- `Position` (Vector2) — coordonnées dans le monde
- `Velocity` (Vector2) — vitesse en unités/seconde
- `Rotation` (float, radians) — orientation (0 = vers le haut)
- `AngularVelocity` (float, rad/s)
- `CollisionRadius` (float, abstrait) — rayon de collision

### 3.2 Ship — Vaisseau joueur

| Propriété | Valeur par défaut | Rôle |
|---|---|---|
| `ThrustForce` | 300 u/s² | Accélération en poussée |
| `RotationSpeed` | 3 rad/s (~172°/s) | Vitesse de rotation |
| `MaxSpeed` | 400 u/s | Plafond vitesse |
| `CollisionRadius` | 16 unités | Hitbox circulaire |
| `LivesRemaining` | 3 | Vies initiales |
| `WeaponCooldown` | 0.25s (normal) / 0.08s (rapid fire) | Cadence de tir |
| `DashCooldown` | 3s | Rechargement dash |
| `InvulnerabilityRemaining` | 5s après mort | Protection post-respawn |

Le vaisseau porte aussi l'état de son **dash** (durée restante, activation) et son **score**.

### 3.3 Asteroid — Astéroïde fragmentable

| Taille | PV | Rayon | Vitesse de base |
|---|---|---|---|
| Large | 3 | 48 unités | 60 u/s |
| Medium | 2 | 28 unités | 100 u/s |
| Small | 1 | 14 unités | 160 u/s |

Quand un astéroïde Large ou Medium est détruit, il se fragmente en 2 à 3 entités de taille inférieure héritant partiellement de la vélocité parent (×0.3), avec rotation aléatoire.

### 3.4 Projectile — Tir

- Vitesse : 700 u/s + héritage inertie vaisseau
- Durée de vie : 2 secondes
- Rayon collision : 4 unités
- Spawn à 20 unités devant le vaisseau pour éviter l'auto-collision

### 3.5 Systèmes de simulation

#### PhysicsSystem

Simule l'inertie et le wrap-around toroïdal. Le monde est un tore : quitter un bord ramène de l'opposé.

- **Vaisseaux** : thrust + rotation + drag 0.99 (conservation 99 % de la vélocité par tick)
- **Astéroïdes et projectiles** : déplacement inertiel pur (pas de drag)
- Tick rate : 60 Hz (16.67 ms par tick)
- Dimensions monde : 3200 × 1800 unités

#### CollisionSystem

Détection cercle-cercle pure, sans allocation. Utilise la comparaison de la distance au carré pour éviter une racine carrée inutile.

- Projectile ↔ Vaisseau (exclut propriétaire)
- Astéroïde ↔ Vaisseau (exclut vaisseaux morts)
- Projectile ↔ Astéroïde (filtre entités inactives)

#### WeaponSystem

Gère le cooldown et crée les projectiles. Supporte un mode RapidFire (power-up) réduisant le cooldown de 0.25s à 0.08s.

#### DashSystem

Boost temporaire 2.5× multiplié à la vélocité, durée 0.3s, recharge 3s. Utile pour esquiver une collision imminente ou traverser rapidement une zone dense.

### 3.6 Événements métier

- `PlayerKilledEvent` — déclenché à chaque mort de joueur
- `AsteroidDestroyedEvent` — déclenché à chaque destruction, porte l'ID destructeur et la position (pour spawn power-up)

---

## 4. Couche Shared — Protocole réseau

`AsteroidOnline.Shared` définit l'intégralité du protocole de communication entre le serveur et les clients. Les deux côtés partagent exactement les mêmes structures.

### 4.1 Format de paquet

Chaque message réseau commence par un octet indiquant son type (`PacketType`), suivi du corps sérialisé en binaire (`BinaryReader` / `BinaryWriter`). Il n'y a pas de sérialisation JSON ou XML pour éviter la surcharge lors de diffusions fréquentes.

### 4.2 Types de paquets

**Client → Serveur**

| Type | Canal | Description |
|---|---|---|
| `ConnectRequest` | TCP (Reliable) | Pseudo + couleur choisie |
| `PlayerInput` | UDP (Unreliable) | État clavier compacté, 60 Hz |
| `StartGameRequest` | TCP | Lancement par l'hôte |
| `ReturnToLobbyRequest` | TCP | Retour après GameOver |
| `ColorSelect` | TCP | Changement de couleur en lobby |

**Serveur → Client**

| Type | Canal | Description |
|---|---|---|
| `LobbyJoined` | TCP | Attribution de l'ID joueur |
| `LobbyState` | TCP | Roster complet des connectés |
| `Countdown` | TCP | Compte à rebours avant démarrage |
| `GameStateSnapshot` | UDP | Positions/rotations/vitesses — 20 Hz |
| `PlayerEliminated` | TCP | Notification d'élimination |
| `PlayerKilled` | TCP | Mort d'un joueur (feed HUD) |
| `GameOver` | TCP | Fin de partie + vainqueur |

### 4.3 PlayerInputPacket — Compacté

5 booléens encodés dans un seul octet (bitfield) + timestamp int64. Total : **9 octets** par input.

```
Bit 0 — ThrustForward
Bit 1 — RotateLeft
Bit 2 — RotateRight
Bit 3 — Fire
Bit 4 — Dash
```

### 4.4 GameStateSnapshotPacket — Quantification

C'est le paquet le plus critique en termes de bande passante car il est diffusé 20 fois par seconde à tous les clients. Il utilise une quantification agressive :

| Donnée | Encodage | Octets |
|---|---|---|
| Position X/Y | uint16 mappé sur [0, WorldSize] | 4 |
| Rotation | uint16 mappé sur [0, 2π] | 2 |
| Vélocité X/Y | sbyte mappé sur [-MaxVel, +MaxVel] | 2 |
| Couleur | byte (enum PlayerColor) | 1 |
| Flags (alive, invulnerable) | bitfield byte | 1 |
| Score | uint16 | 2 |
| Vies | byte | 1 |
| Dash progress | byte (0-255 → 0.0-1.0) | 1 |

Limites par snapshot : 255 joueurs, 28 astéroïdes, 36 projectiles. Ces plafonds ont été calibrés pour rester sous la limite UDP de LiteNetLib et éviter les `TooBigPacketException`.

### 4.5 Interpolation côté client

L'interpolation des positions utilise le **chemin torique le plus court**. Sans ce correctif, un vaisseau passant de X=3190 à X=10 (wrap-around) générerait un Lerp de 3180 unités au lieu de 20, créant un artefact visuel massif.

---

## 5. Couche GameLogic — Interfaces applicatives

`AsteroidOnline.GameLogic` définit les contrats utilisés par le client sans qu'il ait à connaître l'implémentation concrète.

- `INetworkClientService` : connexion, envoi de paquets, polling des événements entrants
- `INavigationService` : navigation entre vues (ConnectView → LobbyView → GameView → GameOverView)

Ce niveau d'indirection permet de :
1. Tester le client avec des services réseau bouchonnés
2. Remplacer LiteNetLib par un autre backend sans toucher au code client

---

## 6. Couche Infrastructure — Réseau client

`LiteNetClientService` implémente `INetworkClientService` via LiteNetLib.

- Connexion UDP vers l'adresse/port du serveur
- Channel 0 : `ReliableOrdered` pour événements garantis (connexion, GameOver, etc.)
- Channel 1 : `Unreliable` pour inputs et snapshots temps-réel
- Polling déclenché par un `DispatcherTimer` 60 Hz dans `App.axaml.cs`

---

## 7. Serveur autoritaire

### 7.1 Rôle et principes

Le serveur est **la seule source de vérité** de l'état de jeu. Les clients n'ont pas autorité sur les positions ou les collisions : ils envoient uniquement leurs **inputs** et reçoivent des **snapshots** de l'état calculé par le serveur.

Ce modèle élimine les désynchronisations et rend toute forme de triche (speed hack, position hack) inefficace.

### 7.2 GameLoop.cs

Le cœur du serveur. Une boucle à **60 Hz** qui :

1. Accepte les connexions entrantes (max 20 joueurs)
2. Traite les paquets UDP reçus (inputs clavier)
3. Simule la physique de tous les vaisseaux et astéroïdes
4. Applique le DashSystem et le WeaponSystem
5. Calcule toutes les collisions
6. Diffuse un snapshot UDP toutes les **3 ticks** (20 Hz)
7. Gère les vagues d'astéroïdes (toutes les 30s)
8. Vérifie les conditions de fin de partie

### 7.3 Phases de partie

```
Lobby ──→ Countdown (5s) ──→ Playing ──→ GameOver ──→ Lobby
                                              │
                                    (reset auto après 8s)
```

**Lobby** : Les joueurs se connectent, l'hôte (premier connecté) peut lancer la partie quand il le souhaite. Pas de contrainte de nombre minimum.

**Countdown** : Délai de 5 secondes broadcasté chaque seconde. Laisse le temps aux clients d'afficher le compte à rebours.

**Playing** : Simulation complète à 60 Hz, snapshots à 20 Hz.

**GameOver** : Broadcast du vainqueur, attente des `ReturnToLobbyRequest`. Timeout de 8s pour éviter le blocage si un client ne répond pas. L'hôte peut forcer le retour immédiatement.

### 7.4 Services serveur

**SpawnService** : Cherche une position de spawn sécurisée dans le monde. Jusqu'à 80 tentatives aléatoires, zone de sécurité de 220 unités autour de la position candidate (pas d'astéroïde ni de joueur). Retourne la position la moins dangereuse si aucune position parfaite n'est trouvée.

**AsteroidSpawnService** : Génère les astéroïdes initiaux (10 + joueurs/2) sur les bords du monde. Gère la fragmentation : Large → 2-3 Medium → 2-3 Small. Tire au sort un drop de power-up (20 % de chance) pour Large et Medium.

**WaveManager** : Déclenche une nouvelle vague toutes les 30 secondes, ajoutant 20 % d'astéroïdes supplémentaires. Plafond à 48 astéroïdes actifs simultanément.

---

## 8. Client Avalonia UI

### 8.1 Injection de dépendances manuelle

Le projet n'utilise pas de conteneur DI tiers. L'injection est réalisée manuellement dans `App.axaml.cs` :

```csharp
_networkService   = new LiteNetClientService();
_navigationService = new NavigationService(CreateViewModel, setCurrentViewModel);
_playerSession    = new PlayerSession();
_gameAudioService = new SystemGameAudioService();
```

Ce choix élimine la complexité d'un framework DI pour un projet de cette taille, tout en conservant des contrats clairs via les interfaces.

### 8.2 Navigation MVVM

La navigation entre vues est pilotée par un `ContentControl` dans `MainWindow.axaml` dont la propriété `Content` est liée au `CurrentViewModel` du `MainWindowViewModel`. Changer de vue revient à affecter un nouveau ViewModel.

Le `ViewLocator` résout automatiquement la vue correspondant à un ViewModel par convention de nommage (réflexion).

### 8.3 ViewModels

**ConnectViewModel** : saisie du pseudo, sélection de couleur, adresse serveur. Gère les états Idle / Connecting / Error et navigue vers le Lobby à réception de `LobbyJoinedPacket`.

**LobbyViewModel** : affiche la liste observable des joueurs connectés. Le bouton "Start Game" n'est visible que pour l'hôte. Relance une synchronisation explicite du roster si le délai de réception est dépassé (jusqu'à 6 tentatives).

**GameViewModel** : boucle de gameplay principale. Reçoit les snapshots 20 Hz, envoie les inputs 60 Hz en UDP, déclenche le rendu, affiche le feed d'éliminations et les métriques réseau (ping).

**GameOverViewModel** : affiche le vainqueur et le classement. Permet de retourner au lobby.

### 8.4 PlayerSession

Cache de session persistante pendant toute la durée de connexion :
- ID attribué par le serveur
- Pseudo et couleur choisis
- Roster local (mapping ID → pseudo) pour affichage des kills

Réinitialisé à la déconnexion ou au retour en lobby.

### 8.5 Audio (Windows)

`SystemGameAudioService` joue des fichiers WAV via **WinMM P/Invoke** (API Win32) en mode asynchrone pour ne pas bloquer le thread principal.

Anti-spam intégré : le son de tir ne peut pas être rejoué avant 55 ms, le son d'explosion avant 120 ms. La musique ambiante tourne en boucle via `mciSendString`.

Ce choix évite l'intégration d'un moteur audio externe (OpenAL, BASS, etc.) tout en produisant un feedback sonore correct. La fonctionnalité est désactivée silencieusement sur les plateformes non-Windows.

---

## 9. Rendu 2D

### 9.1 GameRenderer.cs

Le renderer est entièrement custom, sans moteur de jeu tiers. Il dessine sur un `Canvas` Avalonia via l'API `DrawingContext` à chaque tick d'affichage (60 Hz côté client).

### 9.2 Caméra

Centrée sur le vaisseau du joueur local. Si le joueur est mort, la caméra se recentre sur le milieu de la carte. Pas de zoom : le viewport est adapté pour couvrir une zone de 1600 × 900 unités de jeu.

### 9.3 Wrap-around visuel

Pour chaque entité proche d'un bord, le renderer dessine une copie fantôme du côté opposé. Cela rend le passage de bord visuellement fluide, cohérent avec la physique toroïdale.

### 9.4 Entités visuelles

- **Vaisseaux** : triangles colorés. Le joueur local a un contour blanc, les autres un contour bleu pâle. En état d'invulnérabilité : clignotement alpha + halo pulsant.
- **Astéroïdes** : polygones irréguliers gris avec rotation visuelle. La forme est générée pseudo-aléatoirement à partir de l'ID de l'astéroïde (stable entre frames).
- **Projectiles** : points colorés (couleur du tireur).
- **Pseudos** : affichés au-dessus de chaque vaisseau en temps réel.

### 9.5 VFX

- **Explosions** : particules éphémères (aura + sparkles), durée ~0.5s
- **Screen shake** : déplacement aléatoire de la caméra après chaque collision/explosion, intensité proportionnelle à la taille de l'astéroïde
- **Traînées** : historique de positions précédentes pour un effet de traîne sur les vaisseaux rapides

### 9.6 Radar (minimap)

Affiché en coin bas-droit. Représentation réduite de l'intégralité du monde (rapport ~1:10) avec des points codés par couleur pour joueurs, astéroïdes et projectiles.

### 9.7 HUD

| Élément | Position | Contenu |
|---|---|---|
| Score + vies | Haut gauche | Score numérique + icônes vie |
| Barre dash | Bas gauche | Progression du rechargement |
| Joueurs vivants | Haut droit | Compteur "X / N vivants" |
| Ping réseau | Bas droit | Latence en ms |
| Feed éliminations | Centre bas | Scroll des 5 dernières morts |

---

## 10. Flux réseau de bout en bout

```
CLIENT                                     SERVEUR
────────────────────────────────────────────────────────

[Écran Connect]
  Pseudo + Couleur + IP:Port
  ├──→ ConnectRequest [TCP] ──────────────→ Attribue ID
  │                                         Ajoute joueur
  │ ←─────── LobbyJoined [TCP] ────────────┤ (ID=5, hôte=true si 1er)
  │
[Écran Lobby]
  Attend joueurs...
  │ ←─────── LobbyState [TCP] ─────────────┤ (roster complet)
  │
  (Hôte clique "Start")
  ├──→ StartGameRequest [TCP] ────────────→ Phase Countdown
  │ ←─────── Countdown(4) [TCP] ──────────┤
  │ ←─────── Countdown(3) [TCP] ──────────┤
  │ ←─────── Countdown(2) [TCP] ──────────┤
  │ ←─────── Countdown(1) [TCP] ──────────┤
  │ ←─────── Countdown(0) [TCP] ──────────┤ → Phase Playing
  │
[Écran Game]                               Boucle 60 Hz
  ├──→ PlayerInput [UDP] ─────────────────→ Applique thrust/rotation/tir
  ├──→ PlayerInput [UDP] ─────────────────→ Simule physique
  ├──→ ...60 Hz                             Calcule collisions
  │                                         Gère vagues
  │ ←────── GameStateSnapshot [UDP] ──────┤ (toutes 3 ticks = 20 Hz)
  │ ←────── GameStateSnapshot [UDP] ──────┤
  │
  │ ←────── PlayerKilled [TCP] ───────────┤ (joueur X tué, +score joueur Y)
  │ ←────── PlayerEliminated [TCP] ───────┤ (joueur X éliminé définitivement)
  │
  │ ←────── GameOver [TCP] ───────────────┤ (vainqueur = "Joueur 3")
  │
[Écran GameOver]
  ├──→ ReturnToLobbyRequest [TCP] ────────→ Reset état serveur
  │ ←────── LobbyState [TCP] ─────────────┤
[Écran Lobby]
```

---

## 11. Choix techniques et justifications

### 11.1 Clean Architecture

**Décision** : Séparer le projet en couches Domain / Shared / GameLogic / Infrastructure / Server / Client.

**Pourquoi** : La logique métier (physique, collisions, entités) n'est pas polluée par des détails d'implémentation (réseau, framework UI). Cela permet de tester les systèmes de jeu indépendamment du réseau, et de remplacer une couche sans affecter les autres.

### 11.2 Serveur autoritaire

**Décision** : Le serveur simule intégralement l'état de jeu ; les clients n'envoient que des inputs.

**Pourquoi** : Dans tout jeu multijoueur en réseau, laisser les clients calculer leur propre position crée des désynchronisations et des vecteurs de triche (modification de vélocité, téléportation). Le modèle autoritaire centralise la simulation et distribue uniquement les résultats.

### 11.3 LiteNetLib plutôt qu'un autre backend

**Décision** : Utiliser LiteNetLib 2.1.2 pour le transport réseau.

**Pourquoi** : LiteNetLib est une bibliothèque UDP légère, sans overhead d'un framework réseau de jeu complet (pas de Netcode for GameObjects, pas de Mirror, pas de Photon). Elle offre deux modes de livraison (fiable et non-fiable) qui correspondent exactement aux besoins : TCP-like pour les événements rares, UDP pur pour les inputs/snapshots fréquents.

### 11.4 Sérialisation binaire

**Décision** : `BinaryReader` / `BinaryWriter` pour tous les paquets.

**Pourquoi** : Un snapshot broadcast 20 fois par seconde à 20 joueurs représente 400 envois/seconde. JSON ou MessagePack ajouteraient une surcharge inutile. La sérialisation binaire manuelle produit des paquets de taille minimale et prévisible.

### 11.5 Quantification du snapshot

**Décision** : Encoder positions, angles et vitesses avec des types compacts (uint16, sbyte) plutôt que des float32.

**Pourquoi** : Un float32 non compressé = 4 octets. Un uint16 quantifié = 2 octets. Pour un monde 3200×1800, la précision est de 3200/65535 ≈ 0.05 unité, amplement suffisante pour un rendu fluide. La réduction de taille paquet permet de rester sous la limite UDP de LiteNetLib, ce qui a résolu les `TooBigPacketException` rencontrés en phase de développement.

### 11.6 AvaloniaUI pour le client

**Décision** : Framework XAML multi-plateforme plutôt que WinForms, WPF ou MonoGame.

**Pourquoi** :
- AvaloniaUI offre MVVM natif avec bindings et `ObservableProperty` (CommunityToolkit).
- Le rendu personnalisé sur `Canvas` via `DrawingContext` suffit pour la complexité graphique du jeu.
- Il évite d'embarquer un moteur de jeu complet (Unity, Godot) pour une application relativement simple.
- La compatibilité macOS/Linux est possible sans changer le code UI (seul l'audio Windows est limité).

### 11.7 Pas de framework DI

**Décision** : Injection manuelle dans `App.axaml.cs` plutôt que Microsoft.Extensions.DependencyInjection ou Autofac.

**Pourquoi** : Le graphe de dépendances est simple et stable. Un conteneur DI apporterait de la magie invisible sans bénéfice mesurable. L'injection manuelle est plus lisible, plus facile à déboguer et ne crée pas de dépendance runtime supplémentaire.

### 11.8 Audio WinMM natif

**Décision** : Appels P/Invoke vers `winmm.dll` plutôt qu'un moteur audio cross-platform.

**Pourquoi** : Le feedback audio (tirs, explosions) est une fonctionnalité d'agrément, pas critique. Intégrer OpenAL, NAudio ou FMOD aurait été disproportionné. WinMM permet de jouer des WAV asynchrones en quelques lignes, avec une latence acceptable et zéro dépendance externe.

### 11.9 Interpolation torique côté client

**Décision** : Calculer le chemin le plus court sur le tore pour interpoler les positions.

**Pourquoi** : Sans cette correction, un vaisseau à X=3190 qui reçoit un snapshot à X=10 (après wrap-around) serait interpolé via un trajet linéaire de ~3180 unités, produisant un saut visuel brutal traversant toute la carte. L'interpolation torique détecte que le chemin réel est de 20 unités dans l'autre sens et produit un mouvement fluide.

### 11.10 Retour lobby sans blocage

**Décision** : Le retour en lobby n'attend pas tous les clients. L'hôte force le reset immédiat ; sinon, un timeout de 8s déclenche le reset automatique.

**Pourquoi** : Attendre `n` confirmations réseau dans un contexte UDP peut bloquer indéfiniment si un client se déconnecte sans `ReturnToLobbyRequest`. Ce design garantit que le serveur ne reste jamais bloqué en état GameOver.

### 11.11 Démarrage solo autorisé

**Décision** : Aucune contrainte de nombre minimum de joueurs pour démarrer une partie.

**Pourquoi** : Cela simplifie le flux de développement et de test. La logique métier (phases, physique, GameOver) est identique en solo et en multijoueur. Imposer un seuil minimum serait une contrainte UX sans bénéfice technique.

---

## 12. Constantes et paramètres de jeu

| Paramètre | Valeur | Localisation |
|---|---|---|
| Tick rate serveur | 60 Hz | `GameLoop` |
| Snapshot rate | 20 Hz (1 toutes 3 ticks) | `SnapshotEveryNTicks = 3` |
| Port serveur | 7777 | `GameLoop` |
| Max joueurs | 20 | `MaxPlayers` |
| Dimensions monde | 3200 × 1800 unités | `WorldBounds.Default` |
| Vitesse projectile | 700 u/s | `Projectile.Speed` |
| Vitesse max vaisseau | 400 u/s | `Ship.MaxSpeed` |
| Force poussée | 300 u/s² | `Ship.ThrustForce` |
| Vitesse rotation | 3 rad/s | `Ship.RotationSpeed` |
| Drag | 0.99 | `PhysicsSystem` |
| Cooldown arme normal | 0.25s | `WeaponSystem` |
| Cooldown arme rapid fire | 0.08s | `WeaponSystem` |
| Durée dash | 0.3s | `DashSystem` |
| Cooldown dash | 3s | `DashSystem` |
| Multiplicateur dash | 2.5× | `DashSystem` |
| Vies initiales | 3 | `StartingLives` |
| Invulnérabilité post-mort | 5s | `InvulnerabilitySecondsOnHit` |
| Durée vie projectile | 2s | `Projectile.LifetimeRemaining` |
| Intervalle vague | 30s | `WaveManager` |
| Max astéroïdes | 48 | `WaveManager.MaxAsteroids` |
| Astéroïdes initiaux | 10 + (joueurs / 2) | `StartGame()` |
| Chance power-up | 20 % | `AsteroidSpawnService` |
| Zone sécurité spawn | 220 unités | `SpawnService` |
| Timeout GameOver | 8s | `GameOverAutoReturnDelaySeconds` |
| Compte à rebours | 5s | `CountdownSeconds` |

---

## 13. Déploiement

### 13.1 Serveur

**Prérequis** : .NET 10 Runtime, port 7777 UDP/TCP ouvert.

```bash
cd src/AsteroidOnline.Server
dotnet publish -c Release -o ./publish
./publish/AsteroidOnline.Server
```

**Service systemd** (Linux, recommandé pour auto-restart) :

```ini
[Unit]
Description=AsteroidOnline Game Server
After=network.target

[Service]
Type=simple
User=gameserver
WorkingDirectory=/opt/asteroid-server
ExecStart=/usr/bin/dotnet /opt/asteroid-server/AsteroidOnline.Server.dll
Restart=on-failure
RestartSec=10

[Install]
WantedBy=multi-user.target
```

**Commandes utiles** :

| Action | Commande |
|---|---|
| Voir les logs en direct | `journalctl -u asteroid-server -f` |
| Redémarrer | `sudo systemctl restart asteroid-server` |
| Arrêter | `sudo systemctl stop asteroid-server` |
| Vérifier l'état | `sudo systemctl status asteroid-server` |
| Dernières erreurs | `journalctl -u asteroid-server -n 50` |

### 13.2 Client

**Prérequis** : .NET 10 Runtime, Windows (pour l'audio natif ; UI fonctionnelle sur Linux/macOS sans audio).

```bash
cd src/AsteroidOnline.Client
dotnet run
```

Au lancement, saisir l'adresse IP du serveur et le pseudo. Le port par défaut est 7777.

**Contrôles** :

| Action | Touches |
|---|---|
| Propulsion | `Z` / `W` / `↑` |
| Rotation gauche | `Q` / `A` / `←` |
| Rotation droite | `D` / `→` |
| Tirer | `Espace` / `F` |
| Dash | `Shift` / `E` |

---

## 14. Limites connues et pistes d'évolution

### Limites actuelles

| Limite | Cause | Impact |
|---|---|---|
| Boucle serveur mono-thread | Simplicité initiale | Scalabilité plafonnée à ~20 joueurs simultanés |
| Audio Windows uniquement | P/Invoke WinMM | Clients Linux/macOS muets |
| Pas de reconnexion | Non implémenté | Crash réseau = partie perdue |
| Pas de persistance | Aucune base de données | Scores et historiques non conservés |
| Snapshot non-fiable | UDP sans acknowledgment | Perte possible à haute congestion réseau |

### Pistes d'évolution envisageables

- **Reconnexion client** : mémoriser l'état par peer ID, accepter une reconnexion dans un délai de 30s.
- **Multiple rooms** : héberger plusieurs parties en parallèle sur le même processus serveur.
- **Prédiction client** : appliquer localement les inputs avant de recevoir le snapshot pour réduire la latence perçue.
- **Spectateur** : rôle observateur sans entité physique, recevant uniquement les snapshots.
- **Leaderboard persistant** : base de données légère (SQLite) pour conserver les scores.
- **Audio cross-platform** : remplacer WinMM par une bibliothèque portable (OpenAL-Soft, Mini-audio).
- **Power-ups supplémentaires** : bouclier temporaire, munitions guidées, ralentissement de zone.

---

*Document généré à partir du code source, de la documentation technique et de l'historique de développement du projet AsteroidOnline.*
