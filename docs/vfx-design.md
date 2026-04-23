# VFX and Audio

## VFX conservés / ajustés
- Explosions d'astéroïdes.
- Impacts de projectiles.
- Traînées de tirs.
- Feedback visuel de perte de vie.
- Halo et clignotement pendant invulnérabilité.

## Overlay supprimé
- Le quadrillage de fond (debug visuel) a été retiré car il nuisait à la lisibilité.

## Audio ajouté

### Tirs
- Déclenché immédiatement sur l'input local de tir.
- Cadence limitée pour éviter le spam sonore.
- Timbre synthétique court et brillant, distinct d'un bip système.
- Priorité de chargement custom: `shot2.mp3`, puis `shot.mp3`, puis `shot.wav`.

### Explosions d'astéroïdes
- Déclenché quand un astéroïde disparaît entre deux snapshots.
- Cadence limitée également.
- Son synthétique plus grave et bruité pour bien différencier l'impact de destruction.

### Musique d'ambiance
- Lecture en boucle pendant la partie.
- Priorité de chargement custom: `ambient.mp3`, `ambience.mp3`, `music.mp3`, puis `ambient.wav`.
- Le fichier doit être placé dans `src/AsteroidOnline.Client/Assets/Audio`.

## Implémentation
- Nouveau service `IGameAudioService` + `SystemGameAudioService`.
- Intégration dans `App` puis injecté dans `GameViewModel` / `GameRenderer`.

## Stabilité
- Fallback silencieux hors Windows ou en cas d'erreur audio.
- Le rendu et la boucle de jeu ne sont jamais bloqués par l'audio.

## État actuel
- Audio gameplay réactivé via un backend asynchrone léger.
- Aucun appel audio bloquant n'est fait dans la boucle de rendu.
