# 🚀 AsteroidOnline — User Stories

> **Projet** : AsteroidOnline — Battle Royale multijoueur inspiré d'Asteroids  
> **Stack** : C# / .NET 10 / AvaloniaUI  
> **Format** : `En tant que <rôle>, je veux <action>, afin de <bénéfice>.`

---

## 🔌 Connexion & Lobby

| ID | User Story | Priorité |
|----|-----------|----------|
| US-01 | En tant que **joueur**, je veux saisir mon pseudo et l'adresse du serveur, afin de pouvoir rejoindre une partie en ligne. | 🔴 Must |
| US-02 | En tant que **joueur**, je veux être placé dans un lobby automatiquement après connexion, afin d'attendre que la partie démarre. | 🔴 Must |
| US-03 | En tant que **joueur**, je veux voir une confirmation visuelle que je suis connecté, afin de savoir que le serveur m'a bien accepté. | 🔴 Must |
| US-04 | En tant que **joueur**, je veux voir le nombre de joueurs actuellement dans le lobby, afin de savoir combien de personnes attendent. | 🟡 Should |
| US-05 | En tant que **joueur**, je veux pouvoir choisir la couleur de mon vaisseau avant de rejoindre, afin de me distinguer des autres joueurs. | 🟢 Nice |
| US-06 | En tant que **joueur**, je veux voir un compte à rebours avant le début de la partie, afin de me préparer mentalement. | 🟡 Should |

---

## 🎮 Pilotage & Physique

| ID | User Story | Priorité |
|----|-----------|----------|
| US-07 | En tant que **joueur**, je veux piloter mon vaisseau avec les touches directionnelles ou WASD/ZQSD, afin de me déplacer librement dans l'espace. | 🔴 Must |
| US-08 | En tant que **joueur**, je veux que mon vaisseau ait une physique avec inertie, afin d'avoir une sensation de flottement réaliste comme dans Asteroids. | 🔴 Must |
| US-09 | En tant que **joueur**, je veux tirer des projectiles avec Espace ou F, afin d'éliminer des astéroïdes et les autres joueurs. | 🔴 Must |
| US-10 | En tant que **joueur**, je veux que mon vaisseau réapparaisse à un endroit aléatoire après avoir rejoint, afin de ne pas toujours partir du même point. | 🟡 Should |
| US-11 | En tant que **joueur**, je veux avoir un boost de vitesse temporaire (dash), afin d'esquiver rapidement les astéroïdes ou les tirs ennemis. | 🟢 Nice |
| US-12 | En tant que **joueur**, je veux que mon vaisseau traverse les bords de l'écran et réapparaisse de l'autre côté (wrap-around), afin d'avoir un terrain de jeu toroïdal infini comme dans le jeu original. | 🟡 Should |

---

## ☄️ Astéroïdes

| ID | User Story | Priorité |
|----|-----------|----------|
| US-13 | En tant que **joueur**, je veux que des astéroïdes apparaissent et se déplacent aléatoirement, afin que la partie soit dynamique et dangereuse. | 🔴 Must |
| US-14 | En tant que **joueur**, je veux que les astéroïdes se fragmentent en plusieurs petits morceaux quand je les détruis, afin de retrouver la mécanique classique du jeu original. | 🔴 Must |
| US-15 | En tant que **joueur**, je veux mourir si un astéroïde me percute, afin que le jeu soit challengeant. | 🔴 Must |
| US-16 | En tant que **joueur**, je veux que la quantité d'astéroïdes augmente au fil du temps, afin que la partie devienne de plus en plus intense et stressante. | 🟡 Should |
| US-17 | En tant que **joueur**, je veux que certains astéroïdes laissent tomber un power-up en mourant, afin d'avoir de la chance et de la récompense lors de la destruction. | 🟢 Nice |
| US-18 | En tant que **joueur**, je veux que les gros astéroïdes soient plus lents mais plus durs à détruire, afin d'avoir de la variété tactique dans la gestion des menaces. | 🟡 Should |

---

## ⚡ Power-ups & Objets spéciaux

| ID | User Story | Priorité |
|----|-----------|----------|
| US-19 | En tant que **joueur**, je veux ramasser un power-up de bouclier pour absorber un impact, afin d'avoir une chance de survie supplémentaire en cas d'erreur. | 🟢 Nice |
| US-20 | En tant que **joueur**, je veux ramasser un power-up de tir rapide (triple shot), afin d'avoir un avantage offensif temporaire sur les autres joueurs. | 🟢 Nice |
| US-21 | En tant que **joueur**, je veux ramasser un power-up d'invisibilité temporaire, afin de me cacher des autres joueurs et préparer une embuscade. | 🟢 Nice |
| US-22 | En tant que **joueur**, je veux ramasser un power-up de mines, afin de poser des pièges dans l'arène pour éliminer les joueurs qui passent dessus. | 🟢 Nice |
| US-23 | En tant que **joueur**, je veux ramasser un power-up de laser continu, afin d'avoir une arme à courte portée mais dévastatrice pendant quelques secondes. | 🟢 Nice |

---

## ⚔️ Battle Royale & Combats

| ID | User Story | Priorité |
|----|-----------|----------|
| US-24 | En tant que **joueur**, je veux pouvoir tirer sur les autres joueurs pour les éliminer, afin de remporter la partie en étant le dernier survivant. | 🔴 Must |
| US-25 | En tant que **joueur**, je veux voir les autres joueurs en temps réel sur mon écran, afin de savoir où ils se trouvent. | 🔴 Must |
| US-26 | En tant que **joueur**, je veux être notifié quand un joueur est éliminé (avec son pseudo), afin de suivre l'évolution de la partie. | 🔴 Must |
| US-27 | En tant que **joueur**, je veux voir s'afficher le nom du vainqueur à la fin de la partie, afin de savoir qui a gagné. | 🔴 Must |
| US-28 | En tant que **joueur**, je veux avoir un score basé sur mes éliminations et ma survie, afin de comparer mes performances avec les autres. | 🟡 Should |
| US-29 | En tant que **joueur éliminé**, je veux pouvoir regarder la suite de la partie en mode spectateur, afin de ne pas être forcé de quitter immédiatement. | 🟢 Nice |
| US-30 | En tant que **joueur**, je veux qu'une zone mortelle (type storm) rétrécisse progressivement l'arène, afin de forcer les joueurs à se rapprocher et accélérer la fin des parties. | 🟢 Nice |

---

## 🌐 Réseau & Synchronisation

| ID | User Story | Priorité |
|----|-----------|----------|
| US-31 | En tant que **joueur**, je veux que les mouvements des autres joueurs soient fluides malgré la latence réseau, afin d'avoir une expérience de jeu agréable. | 🔴 Must |
| US-32 | En tant que **serveur**, je veux être la seule source de vérité (modèle autoritaire), afin d'éviter la triche et les désynchronisations. | 🔴 Must |
| US-33 | En tant que **joueur**, je veux voir mon propre ping affiché dans le HUD, afin de savoir si ma connexion est de bonne qualité. | 🟡 Should |
| US-34 | En tant que **joueur**, je veux que ma session soit maintenue si je subis une micro-coupure réseau de moins de 2 secondes, afin de ne pas être déconnecté pour un petit problème passager. | 🟡 Should |

---

## 🖥️ Interface & UX

| ID | User Story | Priorité |
|----|-----------|----------|
| US-35 | En tant que **joueur**, je veux voir mon score et le nombre de joueurs encore en vie dans le HUD, afin de connaître l'état de la partie à tout moment. | 🔴 Must |
| US-36 | En tant que **joueur**, je veux naviguer automatiquement vers l'écran de jeu après connexion réussie, afin de ne pas rester bloqué sur l'écran de connexion. | 🔴 Must |
| US-37 | En tant que **joueur**, je veux voir une mini-map dans un coin de l'écran avec la position de tous les joueurs et des gros astéroïdes, afin d'avoir une vue d'ensemble de l'arène. | 🟢 Nice |
| US-38 | En tant que **joueur**, je veux voir des effets d'explosion visuels quand un astéroïde ou un joueur est détruit, afin que le jeu soit plus satisfaisant et spectaculaire. | 🟡 Should |
| US-39 | En tant que **joueur**, je veux entendre des effets sonores pour les tirs, explosions et mort, afin d'avoir un retour sensoriel immersif. | 🟡 Should |
| US-40 | En tant que **joueur**, je veux voir le classement en temps réel (kills + rang), afin de savoir où j'en suis par rapport aux autres. | 🟡 Should |
| US-41 | En tant que **joueur**, je veux voir un écran de résultats post-partie avec le classement final, afin d'analyser mes performances. | 🟡 Should |

---

## 🎯 Fun & Game Feel

| ID | User Story | Priorité |
|----|-----------|----------|
| US-42 | En tant que **joueur**, je veux que mon vaisseau laisse une traînée lumineuse derrière lui, afin de rendre les déplacements visuellement stylés. | 🟢 Nice |
| US-43 | En tant que **joueur**, je veux voir l'écran secouer légèrement (screen shake) lors d'une explosion proche, afin de ressentir l'impact physiquement. | 🟢 Nice |
| US-44 | En tant que **joueur**, je veux recevoir un message sarcastique/drôle quand je meurs stupidement (percuté par un astéroïde), afin d'être moins frustré et sourire malgré tout. | 🟢 Nice |
| US-45 | En tant que **dernier survivant**, je veux déclencher une animation de victoire spéciale avec des effets de particules, afin de célébrer ma victoire de manière épique. | 🟢 Nice |
| US-46 | En tant que **joueur**, je veux avoir accès à un mode entraînement solo contre des bots, afin de m'améliorer avant d'affronter de vrais joueurs. | 🟢 Nice |
| US-47 | En tant que **joueur**, je veux que les noms des joueurs s'affichent au-dessus de leur vaisseau, afin de savoir qui est qui dans la mêlée. | 🟡 Should |
| US-48 | En tant que **joueur**, je veux pouvoir écrire dans un chat rapide avec des emotes prédéfinies (GG, RIP, LOL), afin de communiquer avec les autres sans taper au clavier. | 🟢 Nice |

---

## 📊 Légende des priorités

| Icône | Label | Description |
|-------|-------|-------------|
| 🔴 | **Must Have** | Indispensable au MVP — le jeu ne fonctionne pas sans |
| 🟡 | **Should Have** | Fortement recommandé pour une bonne expérience |
| 🟢 | **Nice to Have** | Fun à avoir, mais non bloquant |

---

*Document généré dans le cadre du projet AsteroidOnline — C# / .NET 10 / AvaloniaUI*