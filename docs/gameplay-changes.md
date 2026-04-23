# Gameplay Changes

## 1. Lobby solo + multijoueur

### État corrigé
- Le démarrage n'impose plus de minimum de 2 joueurs.
- Le bouton `Start Game` reste gouverné par la logique d'hôte.
- Un joueur seul peut lancer une partie.
- Plusieurs joueurs peuvent relancer une partie sans changer de serveur.

### Où était la contrainte
- Historiquement, la contrainte venait de la logique serveur de préconditions de démarrage.
- Dans l'état actuel corrigé, `HandleStartGameRequest` valide surtout le rôle hôte et la phase lobby.

## 2. Système de fin de manche et relance

### Cause racine du blocage
- Le serveur attendait que tous les clients renvoient `ReturnToLobbyRequest`.
- Si un client ne répondait pas (latence, fermeture, état UI), la phase restait bloquée en `GameOver`.

### Correction
- Le retour lobby est immédiat si la demande vient de l'hôte.
- Un timeout de sécurité en GameOver force aussi le retour lobby après quelques secondes.
- Les états de match sont réinitialisés (`_gameOverElapsed`, `_currentMatchPlayerCount`, entités, cooldowns, vies).

## 3. Coopération et suppression PvP
- Le tir joueur-versus-joueur reste désactivé.
- Les dégâts sont centrés sur l'environnement (astéroïdes).
- Le mode coop/survie est conservé.

## 4. Vies et invulnérabilité
- 3 vies par joueur.
- 5 secondes d'invulnérabilité après perte de vie.
- Respawn sécurisé avant retour au combat.
