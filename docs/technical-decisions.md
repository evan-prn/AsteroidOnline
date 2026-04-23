# Technical Decisions

## 1. Retour lobby robuste après GameOver

### Décision
Dans `GameLoop`, le retour lobby n'attend plus strictement tous les clients.

### Règle appliquée
- Si l'hôte demande le retour: reset immédiat.
- Sinon, reset quand tous les joueurs prêts.
- Timeout de sécurité en GameOver pour éviter les blocages indéfinis.

### Pourquoi
C'est la correction la plus durable contre les deadlocks réseau/UX.

## 2. Pas de contrainte 2 joueurs minimum

### Décision
Le démarrage n'est plus lié à un seuil de joueurs, mais au rôle hôte + phase lobby.

### Pourquoi
Cela rend le flux cohérent pour solo et multijoueur avec la même logique métier.

## 3. Snapshot compact et borné côté réseau

### Décision
Le `GameStateSnapshotPacket` a été compacté par quantification des positions,
angles, vitesses et compteurs. Le serveur ne diffuse plus un nombre illimité
d'astéroïdes et projectiles dans chaque snapshot.

### Pourquoi
- La cause du freeze était un dépassement de la taille maximale LiteNetLib
  (`TooBigPacketException`).
- La compression des données conserve un rendu lisible tout en gardant des
  snapshots sous la limite UDP.
- Le bornage côté diffusion réduit aussi la densité visuelle et la charge CPU.

## 4. Interpolation compatible wrap-around

### Décision
L'interpolation client des positions utilise maintenant le chemin torique le plus
court au lieu d'un `Lerp` linéaire brut.

### Pourquoi
- Le monde est en wrap-around.
- Une interpolation classique entre `3190 -> 10` provoque un saut visuel massif
  qui ressemble à du rollback.
- Corriger ce point côté client supprime une grosse partie des artefacts sans
  ajouter de latence réseau.

## 5. Audio léger via service dédié

### Décision
Le service audio client reste injecté proprement, mais il joue désormais deux WAV
synthétiques distincts via WinMM en mode asynchrone, avec anti-spam.

### Pourquoi
- Les anciens bips système donnaient un rendu pauvre et pouvaient bloquer le thread.
- Cette solution reste légère, sans moteur audio externe.
- Le déclenchement du son de tir est local au press input pour réduire la latence
  perçue.

## 6. Suppression du quadrillage en renderer

### Décision
Retrait du rendu `DrawBackdropGrid`.

### Pourquoi
C'était un overlay parasite permanent, non aligné avec la lisibilité demandée.
