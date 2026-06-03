# Synthèse orale — AsteroidOnline

> Support de présentation construit à partir des documents techniques, changelog, décisions, optimisations, checklist de tests et notes de déploiement fournis.

---

## 1. Introduction

**AsteroidOnline** est un jeu d'arcade spatial inspiré du classique *Asteroids*, adapté à un contexte moderne avec une architecture client-serveur, du multijoueur, un mode solo, un rendu desktop Avalonia et une simulation autoritaire côté serveur.

L'objectif du projet est double :

1. Reproduire les sensations principales d'Asteroids : inertie, rotation, tir, astéroïdes fragmentables et wrap-around.
2. Construire une base technique propre, maintenable et extensible, capable de gérer une partie réseau fluide.

Le projet a progressivement évolué d'une base vide vers une application complète : connexion, lobby, pilotage, astéroïdes, combat, cycle de partie, optimisations, audio, UI/UX, mode spectateur, classement final et déploiement VPS.

---

## 2. Objectifs fonctionnels du projet

Le projet répond à plusieurs familles de besoins exprimées sous forme de user stories.

### Connexion et lobby

Le joueur doit pouvoir :

- saisir un pseudo ;
- indiquer l'adresse du serveur ;
- choisir une couleur de vaisseau ;
- rejoindre automatiquement un lobby ;
- voir les joueurs connectés ;
- identifier l'hôte ;
- lancer une partie en solo ou en multijoueur selon son rôle.

### Gameplay spatial

Le jeu doit proposer :

- un vaisseau contrôlable au clavier ;
- une physique avec inertie ;
- un tir de projectiles ;
- un dash temporaire ;
- un terrain toroïdal avec wrap-around ;
- des astéroïdes dangereux et fragmentables ;
- une difficulté qui augmente avec le temps.

### Réseau et synchronisation

Le jeu doit aussi permettre :

- une simulation fiable côté serveur ;
- l'envoi d'intentions joueur plutôt que de positions ;
- des snapshots réseau réguliers ;
- une synchronisation fluide entre les clients ;
- une relance propre après la fin d'une manche.

### Expérience utilisateur

Les documents montrent aussi un travail de finition sur :

- la lisibilité du HUD ;
- le radar ;
- les champs de connexion ;
- les effets visuels ;
- l'audio ;
- le mode spectateur ;
- le classement final ;
- la stabilité des performances.

---

## 3. Architecture générale

Le projet repose sur une architecture en couches proche de la Clean Architecture.

```text
AsteroidOnline.Client
    AvaloniaUI, MVVM, rendu, inputs, audio, HUD

AsteroidOnline.Infrastructure
    Implémentation réseau côté client avec LiteNetLib

AsteroidOnline.GameLogic
    Interfaces applicatives, navigation, abstraction réseau

AsteroidOnline.Shared
    Paquets réseau, PacketType, sérialisation binaire

AsteroidOnline.Domain
    Entités, systèmes métier, physique, collisions, événements

AsteroidOnline.Server
    GameLoop, serveur autoritaire, spawn, vagues, snapshots
```

Cette séparation permet de garder une logique claire :

- le **Domain** contient le cœur du jeu et ne dépend pas de l'UI ni du réseau ;
- le **Shared** définit le protocole commun entre client et serveur ;
- le **Server** possède l'autorité sur l'état réel de la partie ;
- le **Client** affiche, interprète les inputs et rend l'expérience jouable ;
- l'**Infrastructure** isole LiteNetLib derrière des interfaces.

Le choix de cette architecture facilite les tests, limite le couplage et permet de faire évoluer le projet sans casser toute la base de code.

---

## 4. Connexion et lobby

Le premier bloc du projet met en place l'infrastructure de connexion.

Avant ce bloc, l'application était essentiellement un squelette Avalonia. Après cette étape, elle devient capable de :

- afficher un écran de connexion ;
- envoyer une demande de connexion au serveur ;
- recevoir une confirmation ;
- naviguer automatiquement vers le lobby ;
- afficher la liste des joueurs ;
- gérer le choix de couleur ;
- afficher un compte à rebours avant le lancement.

Le lobby a ensuite été renforcé pendant les passes de stabilisation :

- l'hôte est clairement identifié ;
- le nombre de joueurs est cohérent ;
- le bouton `Start Game` dépend du rôle hôte ;
- le mode solo peut être lancé directement ;
- une resynchronisation du lobby est possible après un retour depuis l'écran de fin.

Le but de ces corrections est d'éviter les états incohérents, comme un lobby vide, un hôte non identifié ou une partie impossible à relancer.

---

## 5. Pilotage, physique et boucle de jeu

Le deuxième bloc introduit la couche de pilotage et de physique.

Toutes les entités physiques héritent d'une base commune, avec :

- une position ;
- une vitesse ;
- une rotation ;
- une vitesse angulaire ;
- un rayon de collision.

Le vaisseau possède une physique inertielle. Quand le joueur pousse vers l'avant, le vaisseau accélère progressivement au lieu de se déplacer instantanément. La vélocité est conservée partiellement, ce qui donne la sensation de flottement attendue dans un jeu spatial.

Le système de déplacement suit une logique simple :

1. Lecture des inputs.
2. Rotation du vaisseau.
3. Calcul de la direction de poussée.
4. Ajout de la force de propulsion.
5. Limitation de la vitesse maximale.
6. Application d'une légère friction.
7. Déplacement.
8. Wrap-around si l'entité sort des limites du monde.

Les projectiles sont rapides, ont une durée de vie limitée et héritent partiellement de la vitesse du vaisseau, ce qui rend le tir plus cohérent.

Le dash ajoute un boost temporaire, utile pour esquiver une collision ou sortir d'une zone dangereuse.

---

## 6. Astéroïdes et difficulté

Le troisième bloc introduit les astéroïdes.

Ils existent en trois tailles :

| Taille | Points de vie | Rayon | Vitesse |
|---|---:|---:|---:|
| Large | 3 | 48 unités | 60 u/s |
| Medium | 2 | 28 unités | 100 u/s |
| Small | 1 | 14 unités | 160 u/s |

Cette logique crée une variété tactique :

- les gros astéroïdes sont lents mais résistants ;
- les moyens sont intermédiaires ;
- les petits sont rapides, difficiles à éviter, mais fragiles.

Quand un astéroïde Large ou Medium est détruit, il se fragmente en deux à trois astéroïdes plus petits. Cela reprend la mécanique classique d'Asteroids : détruire une menace peut en créer plusieurs nouvelles.

La difficulté augmente aussi grâce à un système de vagues. De nouveaux astéroïdes apparaissent régulièrement, avec un plafond pour éviter de surcharger la partie.

---

## 7. Combat, coopération et fin de partie

Le bloc 5 introduit initialement une logique de combat multijoueur, avec collisions projectile contre joueur, éliminations, feed HUD et fin de partie lorsqu'un seul joueur survit.

Cependant, les changements de gameplay récents indiquent une évolution vers un mode **coopération / survie** :

- le tir joueur contre joueur reste désactivé ;
- les dégâts sont centrés sur l'environnement ;
- les astéroïdes deviennent la menace principale ;
- chaque joueur dispose de trois vies ;
- une invulnérabilité de cinq secondes est appliquée après une perte de vie ;
- le respawn est sécurisé avant le retour au combat.

Ce choix rend l'expérience plus lisible et moins punitive. Le jeu met davantage l'accent sur la survie collective et la gestion des menaces environnementales.

---

## 8. Cycle de session et relance

Un point critique du projet concernait le cycle :

```text
Lobby -> Partie -> Game Over -> Retour lobby -> Nouvelle partie
```

Le problème initial venait du fait que le serveur pouvait attendre que tous les clients renvoient une demande de retour au lobby. Si un client ne répondait pas, à cause d'une fermeture, d'une latence ou d'un état UI incorrect, la partie pouvait rester bloquée en phase `GameOver`.

La correction applique une règle plus robuste :

- si l'hôte demande le retour, le reset est immédiat ;
- sinon, le reset peut attendre les joueurs prêts ;
- un timeout de sécurité force le retour au lobby après quelques secondes ;
- les entités de match sont réinitialisées ;
- les cooldowns, vies, états de combat et compteurs sont remis à zéro.

Cette correction est importante pour la démonstration du projet, car elle permet d'enchaîner plusieurs parties sans redémarrer le client ni le serveur.

---

## 9. Mode solo et mode multijoueur

Le projet prend désormais en charge le solo et le multijoueur avec la même logique globale.

La contrainte historique de deux joueurs minimum a été supprimée. Le lancement dépend maintenant surtout :

- du rôle hôte ;
- de la phase lobby ;
- de l'état de synchronisation.

Un joueur seul peut donc rejoindre le lobby et lancer une partie immédiatement.

Cela simplifie les tests, améliore l'expérience utilisateur et permet de présenter le gameplay sans dépendre d'un second client connecté.

---

## 10. Mode spectateur

Le changelog et les changements gameplay ajoutent un mode spectateur.

Lorsqu'un joueur local est éliminé mais qu'il reste des joueurs vivants :

- le client passe en mode spectateur ;
- la caméra suit un joueur survivant ;
- `Tab` ou `PageDown` permet de passer au joueur suivant ;
- `R` ou `PageUp` permet de revenir au joueur précédent ;
- les commandes de pilotage, tir et dash sont neutralisées.

Ce mode est principalement une logique côté client. Le serveur continue de simuler la partie normalement et garde son autorité sur l'état global.

L'intérêt est d'éviter qu'un joueur éliminé se retrouve bloqué sur un écran statique pendant que les autres continuent à jouer.

---

## 11. Classement final

L'écran de fin de partie affiche désormais un classement final.

Le classement présente :

- le rang ;
- le pseudo ;
- le score ;
- le statut final : survivant ou éliminé.

Le tri se base principalement sur le score décroissant, puis sur l'état survivant, puis sur l'identifiant joueur.

Cette fonctionnalité donne un objectif plus clair au joueur. Même dans une logique coopérative ou survie, le score permet de comparer les performances et de rendre la fin de partie plus satisfaisante.

---

## 12. Réseau et serveur autoritaire

Le serveur est autoritaire : les clients n'envoient pas leurs positions, mais seulement leurs intentions.

Exemples d'intentions :

- avancer ;
- tourner à gauche ;
- tourner à droite ;
- tirer ;
- dasher.

Le serveur reçoit ces inputs, simule le monde à 60 Hz, calcule les collisions et diffuse l'état du jeu aux clients.

Les snapshots réseau sont envoyés à 20 Hz. Ils contiennent les joueurs, astéroïdes et projectiles nécessaires au rendu client.

Ce choix limite :

- la triche ;
- les désynchronisations ;
- les incohérences entre clients.

Les événements critiques, comme les éliminations ou le Game Over, passent par des canaux fiables, tandis que les snapshots fréquents utilisent une approche plus légère.

---

## 13. Optimisations réseau et performance

Les documents récents montrent une passe importante d'optimisation FPS et latence.

### Problèmes identifiés

Plusieurs sources de ralentissement ont été repérées :

- création de snapshots complets à chaque frame ;
- dictionnaires recréés trop souvent ;
- rendu Avalonia basé sur la recréation de contrôles visuels ;
- ouverture et décodage audio à chaque tir ou explosion ;
- snapshots réseau trop lourds ;
- allocations fréquentes dans la boucle serveur ;
- recherches répétées coûteuses côté réseau.

### Corrections appliquées

Les principales corrections sont :

- remplacement du rendu par un contrôle custom utilisant `DrawingContext` ;
- utilisation de `TopLevel.RequestAnimationFrame` au lieu d'un `DispatcherTimer` ;
- réutilisation des structures d'interpolation client ;
- préchargement des sons courts ;
- snapshots réseau bornés par joueur ;
- réutilisation de buffers côté serveur ;
- lookup réseau en O(1) au lieu de scans répétés.

Ces changements visent à stabiliser le framerate, réduire les freezes et améliorer la sensation de fluidité.

---

## 14. Interpolation et wrap-around

Une décision technique importante concerne l'interpolation compatible avec le wrap-around.

Dans un monde toroïdal, un objet qui passe de `3190` à `10` ne traverse pas toute la carte : il franchit simplement le bord.

Une interpolation linéaire classique peut donc produire un saut visuel massif, comme si l'objet revenait brutalement en arrière.

La correction consiste à interpoler par le chemin torique le plus court. Cela réduit l'effet de rollback visuel sans ajouter de latence réseau.

---

## 15. UI, UX et lisibilité

Une passe UI/UX corrige plusieurs problèmes d'ergonomie.

### Radar et panneau commandes

Le panneau d'aide des commandes était placé au même endroit que le radar. Il a été déplacé en bas au centre, tandis que le radar reste en bas à droite.

Le radar est une information tactique permanente ; il doit donc rester visible.

### Nettoyage visuel

Le quadrillage permanent a été supprimé, car il polluait la scène et nuisait à la lisibilité. Les effets conservés sont ceux qui servent directement le gameplay : impacts, explosions, invulnérabilité.

### Champs de connexion

Les champs pseudo, IP et port ont été rendus plus lisibles avec :

- fond blanc ;
- texte noir ;
- focus et survol cohérents ;
- sélection bleue type Windows.

Cela évite les problèmes de contraste liés aux thèmes Avalonia ou aux paramètres système.

### Typographie arcade

Une pile de polices orientée arcade / 8-bit est appliquée globalement pour renforcer l'identité visuelle du jeu.

---

## 16. VFX et audio

Les effets visuels conservés sont :

- explosions d'astéroïdes ;
- impacts de projectiles ;
- traînées de tirs ;
- feedback de perte de vie ;
- halo et clignotement pendant l'invulnérabilité.

L'audio ajoute :

- un son de tir déclenché localement dès l'appui ;
- un son d'explosion lors de la destruction d'astéroïdes ;
- une ambiance en boucle pendant la partie.

Les sons courts sont préchargés afin d'éviter les micro-freezes. L'ambiance reste indépendante des effets sonores.

Le service audio est isolé dans `IGameAudioService` et `SystemGameAudioService`. En cas d'erreur ou d'environnement non compatible, un fallback silencieux évite de bloquer le rendu ou de faire planter le jeu.

---

## 17. Refactorisation et qualité de code

Une refactorisation globale a permis de renforcer la qualité du projet.

Parmi les corrections importantes :

- réinitialisation du score entre les parties ;
- suppression de code mort ;
- remplacement d'une vitesse de projectile hardcodée par la propriété `Projectile.Speed` ;
- suppression de duplications liées aux dimensions du monde ;
- amélioration des notifications MVVM ;
- ajout de `sealed` sur des systèmes sans état ;
- nettoyage de l'architecture post-refactorisation.

Ces changements rendent le code plus cohérent, plus maintenable et plus sûr pour de futures évolutions.

---

## 18. Tests recommandés

La checklist de tests couvre les scénarios essentiels.

### Lobby

- Un joueur seul peut rejoindre le lobby.
- Le bouton `Start Game` apparaît pour l'hôte.
- Le solo fonctionne depuis le lobby.
- En multijoueur, seul l'hôte peut lancer la partie.

### HUD et rendu

- Le radar reste visible.
- Le panneau commandes ne masque pas le radar.
- Aucun quadrillage parasite n'apparaît.
- Les pseudos restent lisibles.

### Cycle de partie

Scénario critique :

1. Lancer une partie.
2. Aller jusqu'au Game Over.
3. Retourner au lobby.
4. Relancer sans redémarrer client ou serveur.
5. Refaire le cycle en solo puis en multijoueur.

### Audio

- Le son de tir se déclenche sans bloquer le rendu.
- Les explosions ont un son distinct.
- L'ambiance continue pendant les tirs et explosions.
- Les tirs rapides ne provoquent pas de chute FPS visible.

### Réseau et performance

- Aucune `TooBigPacketException`.
- Pas de saut massif au wrap-around.
- Le gameplay reste proche de 60 FPS.
- Les snapshots bornés affichent bien les entités pertinentes autour du joueur.

### Mode spectateur

- Le joueur éliminé passe en spectateur.
- La caméra suit les survivants.
- Les commandes de jeu sont neutralisées.
- Le changement de joueur suivi fonctionne correctement.

---

## 19. Déploiement VPS

Le projet prévoit un déploiement serveur sur VPS.

L'architecture de déploiement est la suivante :

- le **client** est une application desktop Avalonia lancée par les joueurs ;
- le **serveur** est une application console .NET avec LiteNetLib ;
- le serveur tourne sur un VPS ;
- le port UDP utilisé est `7777` ;
- le service est géré avec `systemd`.

Le processus de déploiement comprend :

1. Création du dossier serveur sur le VPS.
2. Publication du serveur en Release pour Linux x64.
3. Transfert des fichiers via `scp`.
4. Mise en place des droits d'exécution.
5. Création du service `asteroid-server.service`.
6. Activation et démarrage du service.
7. Ouverture du port UDP 7777.

Cette configuration permet d'héberger une instance persistante du serveur de jeu.

---

## 20. Conclusion orale

Pour conclure, **AsteroidOnline** est un projet complet qui combine gameplay arcade, architecture logicielle propre et problématiques réseau réelles.

Le projet a avancé par étapes :

1. Mise en place de la connexion et du lobby.
2. Ajout du pilotage, de la physique et du tir.
3. Intégration des astéroïdes et de leur fragmentation.
4. Ajout du serveur autoritaire et des snapshots réseau.
5. Stabilisation du cycle de partie.
6. Support du solo, du multijoueur et du mode spectateur.
7. Amélioration de l'UI, de l'audio et des performances.
8. Préparation des tests et du déploiement VPS.

L'état actuel du projet est donc une base jouable, stabilisée et extensible. Les prochaines évolutions possibles seraient l'ajout de nouveaux power-ups, un équilibrage plus fin de la difficulté, un système de classement persistant, ou encore des effets visuels plus avancés.

---

## 21. Plan conseillé pour une présentation orale

### Introduction — 1 minute

Présenter AsteroidOnline, son inspiration et l'objectif du projet.

### Architecture — 2 minutes

Expliquer les couches Domain, Shared, Server, Client et Infrastructure.

### Gameplay — 3 minutes

Présenter le vaisseau, la physique, les tirs, les astéroïdes, les vies et l'invulnérabilité.

### Réseau — 2 minutes

Expliquer le serveur autoritaire, les inputs client et les snapshots.

### Stabilisation et finalisation — 3 minutes

Parler du retour lobby, du solo, du mode spectateur, du classement, de l'UI, de l'audio et des optimisations FPS.

### Tests et déploiement — 1 minute

Présenter la checklist et le déploiement VPS.

### Conclusion — 1 minute

Insister sur le fait que le projet est jouable, maintenable et évolutif.

---

## 22. Sources utilisées

Documents fournis :

- `project-technical-overview(1).md`
- `user-stories(1).md`
- `bloc1-review(1).md`
- `bloc2-review(1).md`
- `bloc3-review(1).md`
- `bloc5-review(1).md`
- `bloc6-review(1).md`
- `gameplay-changes(1).md`
- `overview(1).md`
- `performance-optimization.md`
- `technical-decisions(1).md`
- `ui-ux-redesign(1).md`
- `vfx-design(1).md`
- `refactoring-review(1).md`
- `testing-checklist(1).md`
- `deployment-review(1).md`
- `changelog.md`
