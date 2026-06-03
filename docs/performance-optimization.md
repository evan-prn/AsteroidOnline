# Optimisation FPS et latence

## Analyse initiale

Le jeu est structuré autour d'un serveur autoritaire et d'un client Avalonia :

- `GameLoop` côté serveur simule le monde à 60 Hz.
- Les snapshots réseau sont envoyés à 20 Hz.
- `GameViewModel` pilote la boucle client via `DispatcherTimer`.
- `GameRenderer` redessine la scène sur un `Canvas` Avalonia.
- `SystemGameAudioService` gère l'ambiance et les sons one-shot.

## Problèmes identifiés

| Priorité | Fichier | Fonction | Cause probable | Impact |
|---|---|---|---|---|
| Haute | `GameViewModel.cs` | `BuildRenderSnapshot` / `InterpolateSnapshots` | Création d'un snapshot complet et de dictionnaires à chaque frame | Allocations 60 Hz, GC, saccades visuelles |
| Haute | `SystemGameAudioService.cs` | `PlayOneShot` | Ouverture et décodage d'un fichier audio à chaque tir/explosion | Micro-freezes lors des tirs et destructions |
| Haute | `GameLoop.cs` | `BroadcastSnapshot` | Diffusion de tous les astéroïdes au lieu de la sélection bornée prévue | Paquets plus lourds, risque de dépassement UDP |
| Moyenne | `GameLoop.cs` | `ProcessCollisions` | `ToList()` dans la boucle serveur | Allocations 60 Hz pendant les collisions |
| Moyenne | `GameLoop.cs` | `HandlePlayerInput` | Recherche joueur par scan `_peers.FirstOrDefault` | Coût répété à chaque paquet input |
| Moyenne | `PlayerSession.cs` | `GetRosterSnapshot` | Copie du dictionnaire des pseudos à chaque frame | Allocations inutiles côté renderer |
| Moyenne | `GameRenderer.cs` | `UpdateTransientVfx` | `ToDictionary` / `ToHashSet` à chaque snapshot | Allocations 20 Hz, pics lors des explosions |

## Corrections appliquées

### Rendu direct Avalonia

`GameCanvas` a été remplacé par `GameCanvasControl`, un contrôle custom qui dessine
la scène via `DrawingContext`.

Pourquoi :
- l'ancien renderer vidait `Canvas.Children` et recréait des `Polygon`, `Ellipse`,
  `Border` et `TextBlock` à chaque frame ;
- ces créations répétées mettaient la pression sur le layout Avalonia et le GC ;
- `DrawingContext` dessine directement dans la passe de rendu Avalonia, ce qui est
  plus adapté à un gameplay 60 FPS.

Le HUD reste en XAML, car il contient peu d'éléments et bénéficie du binding MVVM.

### Cadence de frame via RequestAnimationFrame

La boucle de gameplay client n'est plus pilotée par un `DispatcherTimer` à 16 ms.
Elle utilise désormais `TopLevel.RequestAnimationFrame`.

Pourquoi :
- un `DispatcherTimer` peut dériver si le traitement de frame prend du temps ;
- un intervalle demandé à 16 ms peut se transformer en environ 24 ms effectifs,
  soit environ 41 FPS ;
- `RequestAnimationFrame` cale la boucle sur la cadence de rendu Avalonia et donne
  un compteur FPS plus représentatif du rendu réel.

### Interpolation client réutilisable

`GameViewModel` réutilise désormais un `GameStateSnapshotPacket` interne et des objets snapshot mis en cache.
Les dictionnaires d'index du snapshot précédent sont reconstruits uniquement à la réception d'un nouveau snapshot.

Pourquoi :
- l'interpolation reste identique visuellement ;
- les allocations à 60 Hz sont fortement réduites ;
- le GC est moins sollicité pendant le gameplay.

### Audio préchargé

Les sons courts sont décodés une fois dans `SystemGameAudioService` via `CachedSound`.
Les tirs et explosions créent seulement un petit provider sur un buffer déjà chargé.

Pourquoi :
- pas d'ouverture fichier pendant le gameplay ;
- pas de décodage MP3/WAV au moment du tir ;
- meilleure stabilité de FPS pendant les événements sonores.

Une limite de 8 one-shots simultanés évite aussi la saturation audio lors de cascades d'explosions.

### Snapshot réseau borné

`BroadcastSnapshot` construit maintenant un snapshot par joueur destinataire.
Chaque client reçoit les astéroïdes et projectiles les plus proches de sa caméra.

Pourquoi :
- évite les paquets trop gros ;
- réduit la charge réseau ;
- limite les pics côté client au moment de désérialiser et rendre la scène.
- améliore la lisibilité locale car la sélection suit le joueur.

Important : le serveur continue de simuler tous les astéroïdes actifs sur toute
la carte. Seul le snapshot client est borné.

### Buffers serveur réutilisés

`GameLoop` réutilise des listes pour :
- projectiles expirés ;
- collisions projectile / astéroïde ;
- vaisseaux testés en collision ;
- bloqueurs de spawn au respawn.

Pourquoi :
- réduit les allocations dans la boucle 60 Hz ;
- stabilise les collisions et respawns ;
- évite les pics GC lors de scènes denses.

### Lookup réseau O(1)

Un dictionnaire inverse `NetPeer -> playerId` évite de parcourir `_peers` à chaque paquet.

Pourquoi :
- les inputs arrivent très souvent ;
- le coût reste stable quand le lobby monte vers 20 joueurs.

## Audio et FPS

Le système audio précédent pouvait impacter les FPS parce que chaque son court construisait un `AudioFileReader`.
Même avec une lecture asynchrone, cette étape implique accès disque, ouverture codec et initialisation de flux.

La nouvelle logique :
- précharge les SFX ;
- conserve l'ambiance sur un flux dédié ;
- limite les sons simultanés ;
- garde le déclenchement local du tir pour préserver la latence perçue.

## Mesures de validation

La build complète a été validée avec :

```bash
dotnet build AsteroidOnline.slnx --no-restore
```

Résultat : 0 erreur, 0 avertissement.

## Risques restants

- Les collisions astéroïdes restent en parcours direct, acceptable avec les limites actuelles mais améliorable par partition spatiale.
- Les snapshots sont encore sérialisés via `MemoryStream` / `BinaryWriter`, suffisant pour l'instant mais optimisable.
- Le 60 FPS dépend encore du coût global UI, du GPU et du taux de rafraîchissement
  écran. Avalonia coalesce les invalidations visuelles dans sa boucle de rendu,
  proche du comportement VSYNC plateforme.

## Recommandations futures

- Ajouter une grille spatiale serveur pour collisions projectile / astéroïde si la densité augmente.
- Ajouter un compteur simple de temps de tick serveur et de temps de rendu client.
- Profiler une vraie partie à 10-20 joueurs avec tirs continus et explosions en cascade.
