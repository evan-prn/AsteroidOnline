# 🚀 AsteroidOnline

> **Battle Royale multijoueur** inspiré du jeu Asteroids classique — le dernier survivant gagne.

![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-12-239120?style=flat-square&logo=csharp)
![AvaloniaUI](https://img.shields.io/badge/AvaloniaUI-11-8B44AC?style=flat-square)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey?style=flat-square)

---

## 🎮 Concept

AsteroidOnline reprend la mécanique du jeu d'arcade **Asteroids** (1979) et y ajoute une dimension **Battle Royale multijoueur en ligne** :

- Chaque joueur pilote un vaisseau spatial dans une arène parsemée d'astéroïdes
- Les astéroïdes se fragmentent en morceaux plus petits lorsqu'ils sont détruits
- Les joueurs peuvent s'éliminer mutuellement en se tirant dessus
- Le **dernier survivant** remporte la partie

---

## 🏗️ Architecture

Le projet suit une **Clean Architecture** en 6 couches distinctes :

```
AsteroidOnline/
├── src/
│   ├── AsteroidOnline.Domain/        # Entités métier, systèmes de jeu
│   ├── AsteroidOnline.Shared/        # Paquets réseau, DTOs partagés
│   ├── AsteroidOnline.GameLogic/   # Logique applicative, services
│   ├── AsteroidOnline.Infrastructure/# Réseau, rendu, I/O
│   ├── AsteroidOnline.Server/        # Serveur de jeu autoritaire
│   └── AsteroidOnline.Client/        # Client AvaloniaUI (MVVM)
└── tests/
    ├── AsteroidOnline.Domain.Tests/
    └── AsteroidOnline.Server.Tests/
```

### Modèle réseau

```
Client A ──UDP inputs──►┐
Client B ──UDP inputs──►│  Serveur autoritaire (60 Hz)
Client C ──UDP inputs──►│   ├── GameWorld (simulation)
                         │   ├── CollisionSystem
◄──UDP snapshot (20 Hz)──┘   └── SpawnSystem
◄──TCP events (fiable)────────────────────────
```

| Canal | Protocole | Port | Usage |
|-------|-----------|------|-------|
| Connexion / Événements | TCP | 7777 | Lobby, mort, fin de partie |
| Inputs / État | UDP | 7778 | Mouvements temps réel, snapshots |

---

## ⚙️ Prérequis

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows / Linux / macOS

---

## 🚀 Démarrage rapide

### 1. Cloner le dépôt

```bash
git clone https://github.com/ton-pseudo/AsteroidOnline.git
cd AsteroidOnline
```

### 2. Restaurer les dépendances

```bash
dotnet restore
```

### 3. Lancer le serveur

```bash
cd src/AsteroidOnline.Server
dotnet run
```

Le serveur écoute sur **TCP 7777** et **UDP 7778**.

### 4. Lancer le client

```bash
# Dans un nouveau terminal
cd src/AsteroidOnline.Client
dotnet run
```

- Saisissez votre pseudo
- Entrez l'adresse du serveur (`127.0.0.1` pour jouer en local)
- Cliquez **Rejoindre la partie**

### 5. Lancer les tests

```bash
dotnet test
```

---

## 🕹️ Contrôles

| Touche | Action |
|--------|--------|
| `↑` / `W` | Poussée avant |
| `←` / `A` | Rotation gauche |
| `→` / `D` | Rotation droite |
| `Espace` / `F` | Tirer |

---

## 🛠️ Stack technique

| Technologie | Usage |
|-------------|-------|
| C# 12 / .NET 10 | Langage et runtime |
| AvaloniaUI 11 | Framework UI cross-platform |
| CommunityToolkit.Mvvm | Pattern MVVM avec source generators |
| TCP / UDP custom | Communication réseau temps réel |
| xUnit + FluentAssertions | Tests unitaires |

---

## 📐 Principes architecturaux

- **Serveur autoritaire** : la simulation tourne exclusivement côté serveur, aucune triche possible
- **Tick fixe à 60 Hz** : déterminisme garanti, indépendant du matériel client
- **Entity interpolation (~100 ms)** : rendu fluide côté client malgré la latence réseau
- **MVVM strict** : séparation totale entre la logique métier et l'interface
- **Commentaires en français** : convention adoptée sur l'ensemble du projet

---

## 📄 Licence

Ce projet est développé dans un cadre académique.  
© 2026 — Tous droits réservés.