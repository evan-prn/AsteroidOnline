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

## F. Réseau et rendu
- [ ] Aucune `TooBigPacketException` après plusieurs minutes avec tirs, fragments et 20 joueurs simulés.
- [ ] Pas d'effet de saut massif lors du wrap-around gauche/droite ou haut/bas.
- [ ] Les pseudos restent lisibles au-dessus des vaisseaux sans masquer le gameplay.

## G. Régression globale
- [ ] Synchronisation lobby/gameover conservée.
- [ ] Score, vies et invulnérabilité restent cohérents.
- [ ] Aucun blocage de navigation entre vues.
