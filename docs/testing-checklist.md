# Testing Checklist

## A. Lobby solo et multijoueur
- [ ] Un joueur seul rejoint le lobby et voit `Start Game` s'il est hôte.
- [ ] Démarrage solo fonctionnel depuis lobby.
- [ ] Le bouton `Start Game` est disponible en solo même si la synchro lobby réseau
  n'a pas encore renvoyé un état complet.
- [ ] En multi, seul l'hôte peut démarrer.
- [ ] En multi, les non-hôtes ne peuvent pas lancer la partie via paquet direct.

## B. Radar et HUD
- [ ] Le radar reste visible en bas à droite.
- [ ] Le panneau commandes ne recouvre plus le radar.
- [ ] Le HUD reste lisible en résolution desktop et fenêtre réduite.

## C. Quadrillage parasite
- [ ] Aucun quadrillage permanent n'apparaît en jeu.
- [ ] Pas d'overlay debug inattendu pendant les combats.

## D. Relance de partie (scénario critique)
1. [ ] Lancer une partie.
2. [ ] Aller jusqu'à GameOver.
3. [ ] Retourner au lobby.
4. [ ] Relancer une partie sans redémarrer client/serveur.
5. [ ] Refaire le cycle en solo puis en multijoueur.

## E. Audio
- [ ] Son de tir déclenché dès l'appui local sans bloquer le rendu.
- [ ] Son d'explosion déclenché à la destruction d'astéroïde.
- [ ] Pas de saturation sonore lors d'un spam d'actions.
- [ ] Pas de crash si l'audio système est indisponible.
- [ ] L'ambiance reste active pendant les tirs/explosions (pas de coupure).
- [ ] Plusieurs tirs rapides s'entendent en superposition.
- [ ] Un tir répété ne provoque pas de chute FPS visible.
- [ ] Une cascade d'explosions ne sature pas l'audio.

## F. Réseau et rendu
- [ ] Aucune `TooBigPacketException` après plusieurs minutes avec tirs, fragments et 20 joueurs simulés.
- [ ] Pas d'effet de saut massif lors du wrap-around gauche/droite ou haut/bas.
- [ ] Les pseudos restent lisibles au-dessus des vaisseaux sans masquer le gameplay.
- [ ] Le respawn après perte de vie ne freeze pas les autres joueurs.
- [ ] Le compteur FPS reste stable pendant 30 secondes de tirs continus.
- [ ] Le gameplay reste proche de 60 FPS en solo avec tirs continus.
- [ ] Le gameplay reste proche de 60 FPS en multijoueur avec plusieurs projectiles et explosions.
- [ ] Les astéroïdes visibles autour du joueur sont bien reçus même quand la map contient plus d'astéroïdes.
- [ ] Le serveur ne perd pas les collisions avec les astéroïdes hors caméra.

## G. Régression globale
- [ ] Synchronisation lobby/gameover conservée.
- [ ] Score, vies et invulnérabilité restent cohérents.
- [ ] Aucun blocage de navigation entre vues.

## H. Power-up laser
- [ ] Un power-up laser apparaît rarement après destruction d'astéroïdes Large/Medium.
- [ ] Le taux observé reste rare et cohérent avec `15%` sur une longue session.
- [ ] Le bonus est visible sur la map et sur le radar.
- [ ] Le joueur récupère une charge en passant dessus.
- [ ] Le HUD affiche le nombre de charges laser.
- [ ] `C` active le laser si une charge est disponible.
- [ ] Le laser reste actif environ `2.5` secondes.
- [ ] Le rayon détruit rapidement les astéroïdes traversés.
- [ ] Le laser ne fait pas de dégâts aux autres joueurs.
- [ ] Le score augmente correctement lors des destructions par laser.
- [ ] Le retour lobby puis relance réinitialise les charges, power-ups au sol et laser actif.

## I. Mode spectateur
- [ ] Un joueur éliminé passe en spectateur si d'autres joueurs sont encore vivants.
- [ ] La caméra suit un joueur vivant.
- [ ] `Tab` ou `PageDown` change vers le joueur vivant suivant.
- [ ] `R` ou `PageUp` change vers le joueur vivant précédent.
- [ ] Appuyer simultanément sur précédent et suivant ne change pas de cible.
- [ ] Les tirs/dash/pilotage ne contrôlent plus le joueur éliminé.
- [ ] Le mode spectateur ne se déclenche pas pendant une simple perte de vie avec respawn.

## J. Connexion et fin de partie
- [ ] Les champs pseudo, IP et port restent lisibles lorsqu'ils sont focus.
- [ ] Les champs pseudo, IP et port restent lisibles sur un écran à scaling Windows élevé.
- [ ] L'écran de fin affiche un classement final complet.
- [ ] Le classement final est trié par score décroissant.
- [ ] Le classement affiche le statut survivant/éliminé.

