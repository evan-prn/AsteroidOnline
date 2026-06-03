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

## 5. Mode spectateur
- Lorsqu'un joueur local est éliminé mais qu'il reste des joueurs vivants, le client passe en spectateur.
- La caméra suit un joueur encore actif.
- `Tab` ou `PageDown` passe au joueur vivant suivant.
- `R` ou `PageUp` revient au joueur vivant précédent.
- Les commandes de pilotage, tir et dash sont neutralisées côté client pendant la spectation.
- Si les deux commandes de changement sont pressées en même temps, la caméra ne change pas.

### Justification
Le mode spectateur reste une logique de caméra et d'interface côté client.
Le serveur continue de simuler la partie normalement et conserve son autorité sur
l'état des joueurs, projectiles et astéroïdes.

## 6. Classement de fin de partie
- L'écran de fin affiche désormais un classement final.
- Le classement est construit côté client depuis le dernier snapshot reçu.
- Le tri privilégie le score, puis l'état survivant, puis l'identifiant joueur.

### Légende du classement
- `Survivant` : le joueur était encore marqué `IsAlive = true` dans le dernier snapshot connu au moment de l'écran de fin.
- `Elimine` : le joueur était marqué `IsAlive = false` dans ce même snapshot.
- En solo, un joueur peut donc apparaître `Survivant` si la partie se termine alors qu'il n'est pas éliminé dans le dernier état reçu.
- Ce statut n'indique pas un kill PvP ou un vainqueur duel : il décrit uniquement l'état de survie final connu.

### Justification
Le dernier snapshot contient déjà les scores et l'état vivant/éliminé des joueurs.
Réutiliser cette donnée évite d'ajouter un paquet réseau supplémentaire pour une
information purement UI.
