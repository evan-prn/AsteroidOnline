# Déploiement VPS — AsteroidOnline

## Architecture

- **Client** : app desktop Avalonia, lancée par les joueurs sur leur PC
- **Serveur** : app .NET console (LiteNetLib), tourne sur le VPS en UDP port 7777
- Pas de conflit avec le site web existant (qui utilise les ports 80/443)

---

## Premier déploiement

### 1. Sur le VPS — créer le dossier

```bash
ssh ubuntu@141.94.121.13 "sudo mkdir -p /opt/asteroid-server && sudo chown ubuntu:ubuntu /opt/asteroid-server"
```

### 2. Sur la machine locale — compiler le serveur

```bash
cd src/AsteroidOnline.Server
dotnet publish -c Release -r linux-x64 --self-contained true -o ./publish
```

### 3. Sur la machine locale — envoyer les fichiers

```bash
scp -r ./publish/ ubuntu@141.94.121.13:/opt/asteroid-server/
```

### 4. Sur le VPS — corriger l'arborescence et rendre exécutable

```bash
mv /opt/asteroid-server/publish/* /opt/asteroid-server/
rmdir /opt/asteroid-server/publish
chmod +x /opt/asteroid-server/AsteroidOnline.Server
```

### 5. Sur le VPS — créer le service systemd

```bash
sudo nano /etc/systemd/system/asteroid-server.service
```

```ini
[Unit]
Description=AsteroidOnline Game Server
After=network.target

[Service]
ExecStart=/opt/asteroid-server/AsteroidOnline.Server
WorkingDirectory=/opt/asteroid-server
Restart=on-failure
User=ubuntu

[Install]
WantedBy=multi-user.target
```

### 6. Sur le VPS — activer et démarrer

```bash
sudo systemctl daemon-reload
sudo systemctl enable asteroid-server
sudo systemctl start asteroid-server
```

### 7. Sur le VPS — ouvrir le port UDP

```bash
sudo ufw allow 7777/udp
```

---

## Mises à jour futures

### Sur la machine locale

```bash
cd src/AsteroidOnline.Server
dotnet publish -c Release -r linux-x64 --self-contained true -o ./publish
scp -r ./publish/* ubuntu@141.94.121.13:/opt/asteroid-server/
```

### Sur le VPS

```bash
sudo systemctl restart asteroid-server
sudo systemctl status asteroid-server
```

---

## Commandes utiles sur le VPS

| Action | Commande |
|---|---|
| Voir les logs en direct | `journalctl -u asteroid-server -f` |
| Redémarrer le serveur | `sudo systemctl restart asteroid-server` |
| Arrêter le serveur | `sudo systemctl stop asteroid-server` |
| Vérifier l'état | `sudo systemctl status asteroid-server` |
| Voir les dernières erreurs | `journalctl -u asteroid-server -n 50` |

---

## Infos de connexion

- **IP** : `141.94.121.13`
- **Port** : `7777` (UDP)
