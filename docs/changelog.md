# Changelog

## Derniers ajouts

### Mode spectateur
- Ajout d'un mode spectateur cote client lorsque le joueur local est elimine et qu'il reste des joueurs vivants.
- La camera peut suivre les joueurs survivants.
- `Tab` / `PageDown` passent au joueur suivant.
- `R` / `PageUp` passent au joueur precedent.
- Les commandes de pilotage, tir et dash sont neutralisees pendant la spectation.

### Classement final
- Ajout d'un classement sur l'ecran de fin de partie.
- Le classement affiche rang, pseudo, score et statut final.
- Le statut `Survivant` / `Elimine` est base sur le dernier snapshot connu.
- Le tri utilise le score decroissant, puis l'etat survivant, puis l'identifiant joueur.

### Lisibilite des champs de connexion
- Les champs pseudo, IP et port utilisent un rendu utilitaire clair : fond blanc, texte noir.
- Les etats focus et survol gardent le meme contraste pour eviter les effets de theme illisibles.
- La selection texte garde une surbrillance bleue type Windows avec texte blanc.

### Performance et rendu
- Le rendu gameplay utilise un controle custom avec `DrawingContext` au lieu de recreer des controles Avalonia a chaque frame.
- La boucle client utilise `TopLevel.RequestAnimationFrame` pour suivre plus proprement la cadence de rendu.
- Les snapshots reseau sont bornes par joueur afin d'envoyer en priorite les entites proches de la camera.

### Audio
- Les sons courts sont precharges pour eviter le decodage pendant les tirs et explosions.
- L'ambiance reste independante des effets sonores.

### Documentation technique
- Ajout d'un guide des points de modification majeurs du code.
- Le guide indique ou modifier les controles, vitesses, asteroides, map, joueurs, rendu, audio, reseau et cycle de partie.
- Les unites de gameplay sont precisees : positions en unites de monde, vitesses en unites de monde par seconde, rotations en radians par seconde.

## Notes
- Ce changelog regroupe les ajouts stables par fonctionnalite.
- Les iterations intermediaires de correction UI ne sont pas listees comme changements separes.
