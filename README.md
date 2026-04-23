# AsteroidOnline

> Jeu spatial coopératif multijoueur inspiré de l'arcade Asteroids.

## Concept
AsteroidOnline reprend les sensations d'Asteroids et les adapte à une boucle multijoueur moderne :
- pilotage inertiel en arène spatiale ;
- destruction et fragmentation d'astéroïdes ;
- coopération entre joueurs contre l'environnement ;
- parties jouables en solo comme en multijoueur.

## Architecture
Le projet est organisé en couches :
- `src/AsteroidOnline.Domain` : entités métier et systèmes de jeu ;
- `src/AsteroidOnline.Shared` : paquets réseau partagés ;
- `src/AsteroidOnline.GameLogic` : interfaces applicatives ;
- `src/AsteroidOnline.Infrastructure` : implémentations techniques (réseau client) ;
- `src/AsteroidOnline.Server` : serveur autoritaire (simulation) ;
- `src/AsteroidOnline.Client` : client Avalonia UI (MVVM + rendu).

## Réseau
- Serveur autoritaire (ticks fixes) ;
- entrées envoyées par les clients ;
- snapshots d'état diffusés aux clients.

## Démarrage rapide
1. Restaurer les dépendances :
```bash
dotnet restore
```
2. Lancer le serveur :
```bash
cd src/AsteroidOnline.Server
dotnet run
```
3. Lancer un ou plusieurs clients :
```bash
cd src/AsteroidOnline.Client
dotnet run
```

## Contrôles
- `WASD` / `ZQSD` / flèches : piloter
- `Espace` / `F` : tirer
- `Shift` / `E` : dash

## Fonctionnalités clés
- 3 vies par joueur ;
- invulnérabilité temporaire après perte de vie ;
- lobby avec bouton `Start Game` réservé à l'hôte ;
- support jusqu'à 20 joueurs ;
- radar et HUD modernisés ;
- VFX et feedback audio (tirs + explosions).

## Tests
```bash
dotnet test
```

## Licence
Projet académique / démonstratif.
