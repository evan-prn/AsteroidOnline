# UI/UX Redesign

## Correction ergonomique radar / panneau commandes

### Problème
Le panneau de rappel des commandes était en bas à droite, sur la même zone que le radar.

### Correction
- Le panneau commandes est déplacé en bas-centre.
- Le radar conserve son emplacement bas-droite.

### Justification UX
- Le radar est une information tactique continue, il doit rester visible en permanence.
- Le panneau commandes est une aide secondaire: son positionnement central bas limite les conflits visuels.
- La hiérarchie de lecture est améliorée: infos critiques en coins stables, aides au centre bas.

## Nettoyage visuel du gameplay
- Suppression du quadrillage permanent qui polluait la scène.
- Conservation des VFX utiles (impacts, explosions, invulnérabilité) uniquement.

## Cohérence Avalonia
- Le layout reste en bindings MVVM.
- Les corrections sont localisées à la vue de jeu et au renderer, sans casser l'architecture existante.
