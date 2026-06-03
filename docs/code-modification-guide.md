# Guide des points de modification majeurs

Ce document indique ou modifier les fonctionnalites importantes du jeu sans repartir de zero. Le projet est organise autour d'un serveur autoritaire : les regles de gameplay doivent donc etre changees cote `Domain` ou `Server`, tandis que le client gere surtout l'affichage, l'audio, les inputs locaux et l'UX.

## Architecture rapide

- `src/AsteroidOnline.Domain` contient les entites et systemes de gameplay purs : vaisseaux, asteroides, projectiles, physique, armes, dash, collisions.
- `src/AsteroidOnline.Server` contient la boucle serveur autoritaire : cycle lobby/partie/fin, connexions, limites joueurs, spawn, vagues, collisions et snapshots reseau.
- `src/AsteroidOnline.Shared` contient les paquets reseau et DTO communs client/serveur.
- `src/AsteroidOnline.Client` contient Avalonia UI, rendu, input, audio, viewmodels et navigation.

## Unite de jeu

Le jeu n'utilise pas directement des pixels pour la simulation. Les positions et vitesses utilisent des `Vector2` en unites de monde.

- Position : unite de monde.
- Vitesse : unite de monde par seconde, notee `u/s` dans le code.
- Acceleration/poussee : unite de monde par seconde carree, notee `u/s2`.
- Rotation : radians par seconde.
- Temps : secondes dans la simulation, via `deltaTime`.

Exemple : dans `src/AsteroidOnline.Domain/Entities/Asteroid.cs`, un gros asteroide a une vitesse de base de `60f`. Cela signifie environ `60` unites de monde par seconde. Le rendu convertit ensuite ces unites en pixels selon la camera et la taille de la fenetre.

La map par defaut est definie dans `src/AsteroidOnline.Domain/World/WorldBounds.cs` :

```csharp
public static readonly WorldBounds Default = new() { Width = 3200f, Height = 1800f };
```

## Modifier les controles

Point principal :

- `src/AsteroidOnline.Client/Input/InputHandler.cs`

La methode `GetCurrentState()` mappe les touches physiques vers les actions :

- Avancer : `W`, `Z`, `Up`
- Tourner a gauche : `A`, `Q`, `Left`
- Tourner a droite : `D`, `Right`
- Tirer : `Space`, `F`
- Dash : `LeftShift`, `RightShift`, `E`
- Spectateur suivant : `Tab`, `PageDown`
- Spectateur precedent : `R`, `PageUp`

La methode `IsGameKey(Key key)` doit aussi etre mise a jour si une nouvelle touche doit etre capturee par le jeu. Sinon, Avalonia peut laisser passer la touche vers les champs texte ou le focus UI.

Si l'action existe deja, changer uniquement `InputHandler.cs` suffit. Si tu ajoutes une nouvelle action gameplay serveur, il faut aussi modifier :

- `src/AsteroidOnline.Client/Input/PlayerInputState.cs`
- `src/AsteroidOnline.Shared/Packets/PlayerInputPacket.cs`
- `src/AsteroidOnline.Server/GameLoop.cs`

Raison technique : le client lit le clavier, mais le serveur reste autoritaire. Une nouvelle action qui influence le gameplay doit donc etre serialisee dans le paquet d'input, envoyee au serveur, puis appliquee dans la boucle de jeu.

## Modifier le mouvement du vaisseau

Point principal :

- `src/AsteroidOnline.Domain/Entities/Ship.cs`

Parametres importants :

- `ThrustForce = 300f` : acceleration du vaisseau en `u/s2`.
- `RotationSpeed = 3f` : vitesse de rotation en radians/seconde.
- `MaxSpeed = 400f` : vitesse maximale du vaisseau en `u/s`.
- `CollisionRadius = 16f` : rayon de collision en unites de monde.

Systeme qui applique ces valeurs :

- `src/AsteroidOnline.Domain/Systems/PhysicsSystem.cs`

La methode `Tick(Ship ship, ...)` applique la rotation, la poussee, le clamp de vitesse, le drag et le wrap-around de la map.

Point d'attention : augmenter `MaxSpeed` sans ajuster la camera, les collisions ou la densite d'asteroides peut rendre le jeu plus nerveux mais moins lisible.

## Modifier la vitesse des asteroides

Point principal :

- `src/AsteroidOnline.Domain/Entities/Asteroid.cs`

La vitesse de base est definie par taille :

```csharp
public static float GetBaseSpeed(AsteroidSize size) => size switch
{
    AsteroidSize.Large  =>  60f,
    AsteroidSize.Medium => 100f,
    AsteroidSize.Small  => 160f,
    _                   => 100f,
};
```

Unite : `u/s`, donc unites de monde par seconde.

Cette vitesse est utilisee lors du spawn initial, des vagues et de la fragmentation dans :

- `src/AsteroidOnline.Server/Services/AsteroidSpawnService.cs`

La methode `CreateAsteroid()` transforme la vitesse de base en velocite vectorielle :

```csharp
Velocity = new Vector2(MathF.Cos(finalAngle), MathF.Sin(finalAngle)) * speed
```

Les fragments utilisent aussi `Asteroid.GetBaseSpeed(nextSize.Value)`.

Point d'attention : la vitesse des petits asteroides est volontairement plus haute. Si tu augmentes fortement les valeurs, il faudra surveiller la lisibilite, les collisions et la difficulte.

## Modifier le nombre d'asteroides

Points principaux :

- `src/AsteroidOnline.Server/GameLoop.cs`
- `src/AsteroidOnline.Server/Services/AsteroidSpawnService.cs`
- `src/AsteroidOnline.Server/Services/WaveManager.cs`

Dans `GameLoop.StartGame()`, le nombre initial est adapte au nombre de joueurs :

```csharp
var initialAsteroidCount = Math.Clamp(8 + (_ships.Count / 2), 10, 22);
```

Dans `WaveManager.cs` :

- `WaveInterval = 30f` : une vague toutes les 30 secondes.
- `MaxAsteroids = 48` : plafond d'asteroides simules simultanement.

Dans `AsteroidSpawnService.SpawnWave()` :

- la vague ajoute environ `20%` du nombre actuel ;
- le parametre `maxAsteroids` bloque le depassement.

Important : ne pas confondre les asteroides simules et les asteroides envoyes au client. Le serveur peut simuler plus d'asteroides que le client n'en recoit dans un snapshot.

Limites reseau dans `src/AsteroidOnline.Server/GameLoop.cs` :

- `SnapshotAsteroidLimit = 28`
- `SnapshotProjectileLimit = 36`

Ces limites evitent les paquets UDP trop gros avec LiteNetLib. Si tu augmentes fortement le nombre d'asteroides visibles, il faut verifier la taille des snapshots pour eviter `TooBigPacketException`.

## Modifier la taille de la map

Point principal :

- `src/AsteroidOnline.Domain/World/WorldBounds.cs`

Valeur actuelle :

```csharp
Width = 3200f, Height = 1800f
```

Impacts directs :

- `PhysicsSystem.ApplyWrapAround()` utilise ces dimensions pour faire reapparaitre les entites de l'autre cote.
- `AsteroidSpawnService` utilise ces dimensions pour spawner sur les bords.
- Le radar et la camera utilisent ces dimensions pour representer la position globale.
- Les snapshots reseau quantifient les positions en se basant sur la map.

Pour modifier le cadrage visible sans changer la map, utiliser plutot :

- `src/AsteroidOnline.Client/Rendering/GameRenderer.cs`

Constantes :

```csharp
private const float VisibleWorldWidth = 1600f;
private const float VisibleWorldHeight = 900f;
```

Ces valeurs definissent la portion du monde visible autour de la camera. Les augmenter donne une camera plus dezoomee ; les reduire donne une camera plus proche.

## Modifier le nombre maximum de joueurs

Point principal :

- `src/AsteroidOnline.Server/GameLoop.cs`

Constante actuelle :

```csharp
private const int MaxPlayers = 20;
```

Cette limite est appliquee lors des connexions dans `OnConnectionRequest` :

```csharp
if (_peers.Count >= MaxPlayers)
{
    request.Reject();
    return;
}
```

Si tu veux augmenter la limite au-dessus de 20, il faut aussi revoir :

- `SnapshotAsteroidLimit` et `SnapshotProjectileLimit`, car plus de joueurs signifie plus d'entites visibles et plus de donnees reseau.
- Les spawns initiaux dans `StartGame()`, pour eviter que les joueurs commencent trop proches.
- Le HUD et le lobby, pour garder les listes lisibles.
- La taille de map dans `WorldBounds.Default`.
- Les buffers de collision initialises avec `MaxPlayers`, par exemple `_shipsCollisionBuffer`.

Risque principal : plus de joueurs augmente la charge reseau, le nombre de collisions a verifier et le bruit visuel. Le serveur reste autoritaire, donc c'est lui qui doit rester stable en premier.

## Modifier les vies et l'invulnerabilite

Point principal :

- `src/AsteroidOnline.Server/GameLoop.cs`

Constantes :

```csharp
private const int StartingLives = 3;
private const float InvulnerabilitySecondsOnHit = 5f;
```

Etat joueur :

- `src/AsteroidOnline.Domain/Entities/Ship.cs`

Proprietes :

- `LivesRemaining`
- `InvulnerabilityRemaining`
- `IsInvulnerable`

La perte de vie, l'elimination et le retour en invulnerabilite sont appliques cote serveur. Le client ne doit pas decider seul qu'un joueur est invulnerable, il doit uniquement afficher l'etat recu dans le snapshot.

## Modifier les tirs et projectiles

Points principaux :

- `src/AsteroidOnline.Domain/Systems/WeaponSystem.cs`
- `src/AsteroidOnline.Domain/Entities/Projectile.cs`

Dans `WeaponSystem.cs` :

- `NormalCooldown = 0.25f` : delai entre deux tirs normaux en secondes.
- `RapidFireCooldown = 0.08f` : delai en mode tir rapide.
- `ProjectileSpawnOffset = 20f` : distance de spawn devant le vaisseau en unites de monde.

Dans `Projectile.cs` :

- `Speed = 700f` : vitesse du projectile en `u/s`.
- `LifetimeRemaining = 2f` : duree de vie en secondes.
- `CollisionRadius = 4f` : rayon de collision en unites de monde.

Point d'attention : reduire le cooldown ou augmenter la duree de vie augmente le nombre de projectiles actifs, donc la charge collision et reseau.

## Modifier le dash

Point principal :

- `src/AsteroidOnline.Domain/Systems/DashSystem.cs`

Constantes :

- `DashDuration = 0.3f` : duree de l'impulsion en secondes.
- `CooldownDuration = 3f` : recharge en secondes.
- `DashMultiplier = 2.5f` : multiplicateur applique a la velocite.

Le dash est applique avant la physique du vaisseau. Modifier `DashMultiplier` change fortement la lisibilite et le risque de collisions soudaines.

## Modifier les collisions et degats

Points principaux :

- `src/AsteroidOnline.Domain/Systems/CollisionSystem.cs`
- `src/AsteroidOnline.Domain/Entities/Ship.cs`
- `src/AsteroidOnline.Domain/Entities/Asteroid.cs`
- `src/AsteroidOnline.Domain/Entities/Projectile.cs`
- `src/AsteroidOnline.Server/GameLoop.cs`

Les rayons de collision sont portes par les entites (`CollisionRadius`). Le serveur orchestre ensuite les collisions dans `GameLoop.ProcessCollisions()`.

Le PvP ayant ete desactive, toute modification offensive entre joueurs doit etre faite avec prudence dans `GameLoop.cs` pour ne pas reintroduire de degats joueur contre joueur.

## Modifier le rendu gameplay

Points principaux :

- `src/AsteroidOnline.Client/Rendering/GameRenderer.cs`
- `src/AsteroidOnline.Client/Rendering/GameCanvasControl.cs`

`GameRenderer.cs` dessine :

- les vaisseaux ;
- les pseudos ;
- les asteroides ;
- les projectiles ;
- le radar ;
- les VFX transitoires.

Fonctions utiles :

- `Render(...)` : ordre global de rendu.
- `DrawShip(...)` : forme, halo, invulnerabilite, selection camera.
- `DrawPlayerName(...)` : pseudo au-dessus du vaisseau.
- `DrawAsteroid(...)` : rendu des asteroides.
- `DrawProjectile(...)` : rendu des tirs.
- `DrawRadar(...)` : radar en bas a droite.

Les performances dependront beaucoup du nombre d'objets dessines, du nombre d'effets transitoires et des allocations par frame.

## Modifier le HUD et les menus

Points principaux :

- `src/AsteroidOnline.Client/Views/ConnectView.axaml`
- `src/AsteroidOnline.Client/Views/LobbyView.axaml`
- `src/AsteroidOnline.Client/Views/GameView.axaml`
- `src/AsteroidOnline.Client/Views/GameOverView.axaml`
- `src/AsteroidOnline.Client/App.axaml`

ViewModels associes :

- `src/AsteroidOnline.Client/ViewModels/ConnectViewModel.cs`
- `src/AsteroidOnline.Client/ViewModels/LobbyViewModel.cs`
- `src/AsteroidOnline.Client/ViewModels/GameViewModel.cs`
- `src/AsteroidOnline.Client/ViewModels/GameOverViewModel.cs`

La police globale et les styles communs se modifient dans `App.axaml`. Les champs de connexion ont aussi des styles locaux dans `ConnectView.axaml` pour garantir un contraste blanc/noir lisible.

## Modifier le son

Points principaux :

- `src/AsteroidOnline.Client/Services/IGameAudioService.cs`
- `src/AsteroidOnline.Client/Services/SystemGameAudioService.cs`
- `src/AsteroidOnline.Client/Assets/Audio`

Les sons courts doivent etre precharges et reutilises. Il faut eviter de charger ou decoder un fichier audio pendant un tir, une explosion ou une frame de rendu.

Pour remplacer un son :

- placer le fichier dans `src/AsteroidOnline.Client/Assets/Audio`;
- garder un nom reconnu par le service, ou ajouter le nom dans `SystemGameAudioService.cs`;
- verifier que le fichier est copie dans le build via le `.csproj`.

L'ambiance doit rester separee des effets one-shot pour eviter qu'un tir coupe la musique de fond.

## Modifier le lobby et le demarrage de partie

Points principaux :

- `src/AsteroidOnline.Client/ViewModels/LobbyViewModel.cs`
- `src/AsteroidOnline.Client/Views/LobbyView.axaml`
- `src/AsteroidOnline.Shared/Packets/StartGameRequestPacket.cs`
- `src/AsteroidOnline.Server/GameLoop.cs`

Le bouton `Start Game` doit rester visible/utilisable par l'hote seulement cote client, mais la securite reelle est cote serveur dans `HandleStartGameRequest(...)`.

Le jeu accepte le solo : ne pas reintroduire de condition du type "minimum 2 joueurs" dans le lobby ou dans `GameLoop.cs`.

## Modifier le mode spectateur

Points principaux :

- `src/AsteroidOnline.Client/ViewModels/GameViewModel.cs`
- `src/AsteroidOnline.Client/Input/InputHandler.cs`
- `src/AsteroidOnline.Client/Rendering/GameRenderer.cs`
- `src/AsteroidOnline.Client/Views/GameView.axaml`

La selection joueur suivant/precedent est lue dans `InputHandler.cs`, appliquee dans le viewmodel, puis transmise au renderer comme cible camera.

## Modifier le classement de fin

Points principaux :

- `src/AsteroidOnline.Client/ViewModels/GameViewModel.cs`
- `src/AsteroidOnline.Client/ViewModels/GameOverViewModel.cs`
- `src/AsteroidOnline.Client/Views/GameOverView.axaml`

Le classement final est construit cote client a partir du dernier snapshot connu. Les statuts `Survivant` et `Elimine` sont des labels UX bases sur l'etat de vie transmis par le serveur.

La legende fonctionnelle est documentee dans `docs/gameplay-changes.md`.

## Modifier les paquets reseau

Points principaux :

- `src/AsteroidOnline.Shared/Packets`
- `src/AsteroidOnline.Server/GameLoop.cs`
- `src/AsteroidOnline.Infrastructure/Networking/LiteNetClientService.cs`

Si tu ajoutes une donnee dans un paquet :

- modifier la classe du paquet dans `Shared`;
- modifier `Serialize(...)` et `Deserialize(...)` dans le meme ordre ;
- adapter le serveur et le client qui consomment ce paquet ;
- verifier la taille finale, surtout pour les snapshots UDP.

Point critique : LiteNetLib limite la taille des paquets simples. Les snapshots gameplay doivent rester compacts ou limites par joueur.

## Modifier le cycle de partie

Point principal :

- `src/AsteroidOnline.Server/GameLoop.cs`

Fonctions importantes :

- `HandleStartGameRequest(...)` : validation du droit de demarrer.
- `EnterCountdown()` : passage lobby vers compte a rebours.
- `StartGame()` : reset et initialisation de manche.
- `TickPlaying(...)` : simulation gameplay.
- `EnterGameOver(...)` : passage en fin de partie.
- `ResetMatchToLobby()` : retour lobby et nettoyage.

Toute modification de cycle doit verifier le scenario complet : lancement, fin de partie, retour lobby, relance.

## Checklist avant de modifier une feature majeure

- Changer la regle cote serveur ou domaine, pas seulement cote client.
- Verifier si la donnee doit etre ajoutee dans un paquet `Shared`.
- Verifier l'impact sur les snapshots reseau.
- Verifier l'impact sur le rendu et le HUD.
- Verifier l'impact solo et multijoueur.
- Verifier le retour lobby puis relance de partie.
- Mettre a jour la documentation si la feature devient configurable ou change de comportement.
