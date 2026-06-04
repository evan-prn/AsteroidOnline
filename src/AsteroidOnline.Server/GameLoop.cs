namespace AsteroidOnline.Server;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using AsteroidOnline.Domain.Entities;
using AsteroidOnline.Domain.Events;
using AsteroidOnline.Domain.Systems;
using AsteroidOnline.Domain.World;
using AsteroidOnline.Server.Services;
using AsteroidOnline.Shared.Packets;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Logging;

/// <summary>
/// Boucle de jeu serveur autoritaire à 60 Hz (US-27).
/// Responsabilités :
/// <list type="bullet">
///   <item>Accepter les connexions LiteNetLib et gérer les sessions joueurs</item>
///   <item>Traiter les inputs reçus des clients</item>
///   <item>Simuler la physique, les collisions et les armes à 60 Hz</item>
///   <item>Diffuser les snapshots d'état à 20 Hz (toutes les 3 ticks)</item>
///   <item>Gérer les vagues d'astéroïdes et la fin de partie</item>
/// </list>
/// </summary>
public sealed class GameLoop : INetEventListener, IDisposable
{
    // ── Constantes ─────────────────────────────────────────────────────────────
    private const int    TickRateHz          = 60;
    private const double TickDurationMs      = 1000.0 / TickRateHz;   // ≈16.67 ms
    private const int    SnapshotEveryNTicks = 3;                      // 20 Hz
    private const string ConnectionKey       = "AsteroidOnline_v1";
    private const int    Port                = 7777;
    private const int    MaxPlayers          = 20;
    private const int    CountdownSeconds    = 5;
    private const int    StartingLives       = 3;
    private const float  InvulnerabilitySecondsOnHit = 5f;
    private const float  GameOverAutoReturnDelaySeconds = 8f;
    private const int    SnapshotAsteroidLimit = 28;
    private const int    SnapshotProjectileLimit = 36;
    private const float  LaserDurationSeconds = 4.5f;
    private const float  LaserLength = 1400f;

    // ── Infrastructure réseau ──────────────────────────────────────────────────
    private readonly NetManager _netManager;
    private readonly ILogger<GameLoop> _logger;

    // ── État du monde ──────────────────────────────────────────────────────────
    private readonly WorldBounds       _bounds   = WorldBounds.Default;
    private readonly Dictionary<int, Ship>      _ships      = new();
    private readonly Dictionary<int, NetPeer>   _peers      = new();
    private readonly Dictionary<NetPeer, int>   _peerPlayerIds = new();
    private readonly Dictionary<int, Asteroid>  _asteroids  = new();
    private readonly Dictionary<int, Projectile> _projectiles = new();
    private readonly Dictionary<int, PowerUp> _powerUps = new();
    private readonly List<Ship> _shipsCollisionBuffer = new(MaxPlayers);
    private readonly List<int> _projectilesToRemove = new(64);
    private readonly List<int> _powerUpsToRemove = new(16);
    private readonly List<(int ProjectileId, int AsteroidId, int OwnerId)> _projectileAsteroidHits = new(64);
    private readonly List<(int AsteroidId, int OwnerId)> _laserAsteroidHits = new(64);
    private readonly List<PhysicalEntity> _spawnBlockersBuffer = new(128);

    // Dernier input connu par joueur (thread-safe, mis à jour côté réseau).
    // Le tick serveur applique toujours la simulation au même rythme, indépendamment
    // du nombre de paquets reçus pendant l'intervalle.
    private readonly ConcurrentDictionary<int, PlayerInputPacket> _latestInputs = new();
    private readonly HashSet<int> _playersReadyForLobby = new();

    // ── Systèmes de jeu ────────────────────────────────────────────────────────
    private readonly PhysicsSystem       _physics    = new();
    private readonly WeaponSystem        _weapon     = new();
    private readonly DashSystem          _dash       = new();
    private readonly CollisionSystem     _collision  = new();
    private readonly SpawnService        _spawnSvc;
    private readonly AsteroidSpawnService _asteroidSvc;
    private readonly WaveManager         _waveManager = new();

    // ── État de la session ─────────────────────────────────────────────────────
    private enum GamePhase { Lobby, Countdown, Playing, GameOver }
    private GamePhase _phase = GamePhase.Lobby;
    private int   _countdownRemaining = CountdownSeconds;
    private float _countdownTimer;
    private float _snapshotAccumulator;
    private float _gameOverElapsed;
    private int _currentMatchPlayerCount;
    private int   _nextPlayerId = 1;
    private int   _nextProjectileId = 1;
    private int   _nextPowerUpId = 5000;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Initialise la GameLoop et démarre l'écoute réseau sur le port <see cref="Port"/>.
    /// </summary>
    /// <param name="logger">Logger Microsoft.Extensions.Logging.</param>
    public GameLoop(ILogger<GameLoop> logger)
    {
        _logger     = logger;
        _spawnSvc   = new SpawnService(_bounds);
        _asteroidSvc = new AsteroidSpawnService(_bounds);

        _netManager = new NetManager(this) { AutoRecycle = true };
        _netManager.Start(Port);
        _logger.LogInformation("Serveur démarré sur le port {Port}", Port);
    }

    // ──── Boucle principale ────────────────────────────────────────────────────

    /// <summary>
    /// Lance la boucle de jeu bloquante.
    /// Utilise un <see cref="Stopwatch"/> haute résolution pour un tick précis à 60 Hz.
    /// </summary>
    public async Task RunAsync(CancellationToken externalCt = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(externalCt, _cts.Token);
        var ct = linked.Token;

        var sw = Stopwatch.StartNew();
        var nextTick = sw.Elapsed.TotalMilliseconds;

        _logger.LogInformation("Boucle de jeu démarrée à {Rate} Hz", TickRateHz);

        while (!ct.IsCancellationRequested)
        {
            var now = sw.Elapsed.TotalMilliseconds;
            if (now < nextTick)
            {
                // Attente courte pour ne pas monopoliser le CPU
                var wait = (int)(nextTick - now);
                if (wait > 1)
                    await Task.Delay(1, ct).ConfigureAwait(false);
                continue;
            }

            var deltaTime = (float)((now - (nextTick - TickDurationMs)) / 1000.0);
            nextTick += TickDurationMs;

            // Traitement des événements réseau
            _netManager.PollEvents();

            // Tick de simulation
            Tick(deltaTime);
        }
    }

    /// <summary>Effectue un tick complet de simulation.</summary>
    private void Tick(float dt)
    {
        switch (_phase)
        {
            case GamePhase.Lobby:
                TickLobby(dt);
                break;

            case GamePhase.Countdown:
                TickCountdown(dt);
                break;

            case GamePhase.Playing:
                TickPlaying(dt);
                break;

            case GamePhase.GameOver:
                TickGameOver(dt);
                break;
        }
    }

    // ──── Phase Lobby ──────────────────────────────────────────────────────────

    private void TickLobby(float dt)
    {
        _ = dt;
    }

    // ──── Phase Countdown ──────────────────────────────────────────────────────

    private void TickCountdown(float dt)
    {
        _countdownTimer += dt;
        if (_countdownTimer < 1f)
            return;

        _countdownTimer -= 1f;
        _countdownRemaining--;
        BroadcastCountdown(_countdownRemaining);

        if (_countdownRemaining <= 0)
            StartGame();
    }

    private void StartGame()
    {
        _phase = GamePhase.Playing;
        _latestInputs.Clear();
        _playersReadyForLobby.Clear();
        _snapshotAccumulator = 0f;
        _gameOverElapsed = 0f;
        _currentMatchPlayerCount = _ships.Count;
        _waveManager.Reset();
        _nextProjectileId = 1;
        _nextPowerUpId = 5000;
        _asteroidSvc.Reset();

        // Spawn des joueurs à des positions sûres
        var allEntities = new List<PhysicalEntity>(_ships.Values.Count + _asteroids.Count);
        allEntities.AddRange(_asteroids.Values);
        foreach (var ship in _ships.Values)
        {
            ship.Position = _spawnSvc.FindSpawnPosition(allEntities);
            ship.IsAlive  = true;
            ship.Velocity = System.Numerics.Vector2.Zero;
            ship.Score    = 0;
            ship.LivesRemaining = StartingLives;
            ship.InvulnerabilityRemaining = 0f;
            ship.DashCooldown = 0f;
            ship.IsDashing = false;
            ship.DashTimeRemaining = 0f;
            ship.WeaponCooldown = 0f;
            ship.LaserCharges = 0;
            ship.LaserRemaining = 0f;
            allEntities.Add(ship);
        }

        _asteroids.Clear();
        _projectiles.Clear();
        _powerUps.Clear();

        // Densité d'astéroïdes adaptée à la taille du lobby.
        var initialAsteroidCount = Math.Clamp(8 + (_ships.Count / 2), 10, 22);
        foreach (var asteroid in _asteroidSvc.SpawnInitialWave(initialAsteroidCount))
            _asteroids[asteroid.Id] = asteroid;

        _logger.LogInformation("Partie démarrée avec {Count} joueur(s)", _ships.Count);
    }

    // ──── Phase Playing ────────────────────────────────────────────────────────

    private void TickPlaying(float dt)
    {
        // 1. Physique + inputs des vaisseaux (une seule simulation par tick).
        foreach (var ship in _ships.Values)
        {
            if (!ship.IsAlive) continue;
            if (ship.InvulnerabilityRemaining > 0f)
                ship.InvulnerabilityRemaining = MathF.Max(0f, ship.InvulnerabilityRemaining - dt);

            _latestInputs.TryGetValue(ship.Id, out var input);
            var thrustForward = input?.ThrustForward ?? false;
            var rotateLeft    = input?.RotateLeft ?? false;
            var rotateRight   = input?.RotateRight ?? false;
            var fire          = input?.Fire ?? false;
            var dash          = input?.Dash ?? false;
            var laser         = input?.Laser ?? false;

            _weapon.UpdateCooldown(ship, dt);
            _dash.Tick(ship, dash, dt);
            TickLaser(ship, laser, dt);
            _physics.Tick(ship, thrustForward, rotateLeft, rotateRight, dt, in _bounds);

            var proj = _weapon.TryFire(ship, fire, _nextProjectileId);
            if (proj is not null)
            {
                _nextProjectileId++;
                _projectiles[proj.Id] = proj;
            }
        }

        // 2. Physique des astéroïdes
        foreach (var asteroid in _asteroids.Values)
            _physics.Tick(asteroid, dt, in _bounds);

        // 3. Physique et durée de vie des projectiles
        _projectilesToRemove.Clear();
        foreach (var proj in _projectiles.Values)
        {
            proj.LifetimeRemaining -= dt;
            if (proj.LifetimeRemaining <= 0f)
            {
                proj.IsActive = false;
                _projectilesToRemove.Add(proj.Id);
                continue;
            }
            _physics.Tick(proj, dt, in _bounds);
        }
        foreach (var id in _projectilesToRemove)
            _projectiles.Remove(id);

        _powerUpsToRemove.Clear();
        foreach (var powerUp in _powerUps.Values)
        {
            powerUp.LifetimeRemaining -= dt;
            if (powerUp.LifetimeRemaining <= 0f)
            {
                powerUp.IsActive = false;
                _powerUpsToRemove.Add(powerUp.Id);
            }
        }
        foreach (var id in _powerUpsToRemove)
            _powerUps.Remove(id);

        // 4. Détection de collisions
        ProcessCollisions();

        // 5. Vagues d'astéroïdes (US-16)
        if (_waveManager.Tick(dt, _asteroids.Count))
        {
            foreach (var asteroid in _asteroidSvc.SpawnWave(_asteroids.Count, WaveManager.MaxAsteroids))
                _asteroids[asteroid.Id] = asteroid;
            _logger.LogInformation("Vague {Wave} déclenchée", _waveManager.CurrentWave);
        }

        // 6. Broadcast snapshot toutes les 3 ticks (20 Hz)
        _snapshotAccumulator += dt;
        if (_snapshotAccumulator >= (TickDurationMs * SnapshotEveryNTicks / 1000f))
        {
            _snapshotAccumulator = 0f;
            BroadcastSnapshot();
        }

        // 7. Vérification fin de partie
        CheckGameOver();
    }

    // ──── Collisions ───────────────────────────────────────────────────────────

    private static void TickLaser(Ship ship, bool laserInput, float dt)
    {
        if (ship.LaserRemaining > 0f)
        {
            ship.LaserRemaining = MathF.Max(0f, ship.LaserRemaining - dt);
            return;
        }

        if (!laserInput || ship.LaserCharges <= 0)
            return;

        ship.LaserCharges--;
        ship.LaserRemaining = LaserDurationSeconds;
    }

    private void ProcessCollisions()
    {
        _shipsCollisionBuffer.Clear();
        foreach (var ship in _ships.Values)
            _shipsCollisionBuffer.Add(ship);

        // Projectile ↔ Astéroïde
        _projectileAsteroidHits.Clear();
        foreach (var proj in _projectiles.Values)
        {
            var hit = _collision.CheckProjectileVsAsteroids(proj, _asteroids.Values);
            if (hit is null) continue;

            proj.IsActive = false;
            _projectileAsteroidHits.Add((proj.Id, hit.Id, proj.OwnerId));
        }

        foreach (var hit in _projectileAsteroidHits)
        {
            _projectiles.Remove(hit.ProjectileId);
            if (_asteroids.TryGetValue(hit.AsteroidId, out var asteroid))
                DamageAsteroid(asteroid, hit.OwnerId);
        }

        _laserAsteroidHits.Clear();
        foreach (var ship in _ships.Values)
        {
            if (!ship.IsAlive || !ship.IsLaserActive)
                continue;

            var direction = new Vector2(MathF.Sin(ship.Rotation), -MathF.Cos(ship.Rotation));
            var end = ship.Position + (direction * LaserLength);
            foreach (var asteroid in _asteroids.Values)
            {
                if (!asteroid.IsActive)
                    continue;

                if (LaserIntersectsAsteroid(ship.Position, end, asteroid.Position, asteroid.CollisionRadius))
                    _laserAsteroidHits.Add((asteroid.Id, ship.Id));
            }
        }

        foreach (var hit in _laserAsteroidHits)
        {
            if (_asteroids.TryGetValue(hit.AsteroidId, out var asteroid))
                DamageAsteroid(asteroid, hit.OwnerId);
        }

        // Astéroïde ↔ Joueur (US-15)
        foreach (var asteroid in _asteroids.Values)
        {
            var victim = _collision.CheckAsteroidVsShip(asteroid, _shipsCollisionBuffer);
            if (victim is null) continue;
            ApplyPlayerDamage(victim, "Astéroïde");
        }

        _powerUpsToRemove.Clear();
        foreach (var ship in _ships.Values)
        {
            var powerUp = _collision.CheckShipVsPowerUps(ship, _powerUps.Values);
            if (powerUp is null)
                continue;

            ApplyPowerUp(ship, powerUp);
            powerUp.IsActive = false;
            _powerUpsToRemove.Add(powerUp.Id);
        }
        foreach (var id in _powerUpsToRemove)
            _powerUps.Remove(id);
    }

    private void DamageAsteroid(Asteroid asteroid, int shooterId)
    {
        var asteroidSize = asteroid.Size;
        asteroid.HitPoints--;
        if (asteroid.HitPoints > 0) return;

        asteroid.IsActive = false;
        _asteroids.Remove(asteroid.Id);

        // Fragmentation (US-14)
        var evt = _asteroidSvc.CreateDestroyedEvent(asteroid);
        foreach (var fragment in evt.NewFragments)
        {
            var newAsteroid = AsteroidSpawnService.CreateFromFragment(fragment);
            _asteroids[newAsteroid.Id] = newAsteroid;
        }

        if (evt.DropsPowerUp)
        {
            var powerUp = new PowerUp
            {
                Id = _nextPowerUpId++,
                Type = PowerUpType.Laser,
                Position = evt.Position,
                LifetimeRemaining = 18f,
                IsActive = true,
            };
            _powerUps[powerUp.Id] = powerUp;
        }

        if (shooterId > 0 && _ships.TryGetValue(shooterId, out var shooter))
            shooter.Score += GetAsteroidScore(asteroidSize);

        _logger.LogDebug("Astéroïde {Id} détruit", asteroid.Id);
    }

    private void ApplyPlayerDamage(Ship victim, string sourceName)
    {
        if (!victim.IsAlive || victim.IsInvulnerable)
            return;

        victim.LivesRemaining = Math.Max(0, victim.LivesRemaining - 1);
        if (victim.LivesRemaining > 0)
        {
            RespawnShipWithInvulnerability(victim);
            _logger.LogDebug(
                "{Victim} perd une vie ({Lives} restante(s))",
                victim.Pseudo,
                victim.LivesRemaining);
            return;
        }

        victim.IsAlive = false;
        victim.InvulnerabilityRemaining = 0f;
        victim.LaserRemaining = 0f;

        var packet = new PlayerEliminatedPacket
        {
            VictimId   = victim.Id,
            VictimName = victim.Pseudo,
            KillerName = sourceName,
        };
        BroadcastReliable(packet);

        _logger.LogInformation("{Victim} éliminé par {Killer}", victim.Pseudo, sourceName);
    }

    private void RespawnShipWithInvulnerability(Ship ship)
    {
        _spawnBlockersBuffer.Clear();
        foreach (var asteroid in _asteroids.Values)
            _spawnBlockersBuffer.Add(asteroid);
        foreach (var otherShip in _ships.Values)
        {
            if (otherShip.IsAlive && otherShip.Id != ship.Id)
                _spawnBlockersBuffer.Add(otherShip);
        }

        ship.Position = _spawnSvc.FindSpawnPosition(_spawnBlockersBuffer);
        ship.Velocity = System.Numerics.Vector2.Zero;
        ship.IsDashing = false;
        ship.DashTimeRemaining = 0f;
        ship.DashCooldown = 0f;
        ship.WeaponCooldown = 0f;
        ship.LaserRemaining = 0f;
        ship.InvulnerabilityRemaining = InvulnerabilitySecondsOnHit;
    }

    private static void ApplyPowerUp(Ship ship, PowerUp powerUp)
    {
        if (powerUp.Type == PowerUpType.Laser)
            ship.LaserCharges = Math.Min(ship.LaserCharges + 1, 3);
    }

    private void CheckGameOver()
    {
        var alive = _ships.Values.Count(s => s.IsAlive);
        if (_ships.Count == 0 || alive > 0)
            return;

        EnterGameOver(winner: null, winnerNameFallback: "Escouade éliminée");
    }

    // ──── Broadcast helpers ────────────────────────────────────────────────────

    private void BroadcastSnapshot()
    {
        foreach (var peer in _peers)
            Send(peer.Value, CreateSnapshotForPlayer(peer.Key), DeliveryMethod.Unreliable);
    }

    private GameStateSnapshotPacket CreateSnapshotForPlayer(int recipientPlayerId)
    {
        var snapshot = new GameStateSnapshotPacket
        {
            ServerTimestamp   = Stopwatch.GetTimestamp(),
            AlivePlayersCount = _ships.Values.Count(s => s.IsAlive),
        };

        foreach (var ship in _ships.Values)
        {
            snapshot.Players.Add(new PlayerSnapshot
            {
                Id                   = ship.Id,
                X                    = ship.Position.X,
                Y                    = ship.Position.Y,
                Rotation             = ship.Rotation,
                VelocityX            = ship.Velocity.X,
                VelocityY            = ship.Velocity.Y,
                Color                = ship.Color,
                IsAlive              = ship.IsAlive,
                DashCooldownProgress = DashSystem.GetCooldownProgress(ship),
                Score                = ship.Score,
                LivesRemaining       = ship.LivesRemaining,
                IsInvulnerable       = ship.IsInvulnerable,
                InvulnerabilityRemaining = ship.InvulnerabilityRemaining,
                LaserCharges         = ship.LaserCharges,
                LaserRemaining       = ship.LaserRemaining,
                IsLaserActive        = ship.IsLaserActive,
            });
        }

        foreach (var asteroid in SelectAsteroidsForSnapshot(recipientPlayerId))
        {
            snapshot.Asteroids.Add(new AsteroidSnapshot
            {
                Id        = asteroid.Id,
                X         = asteroid.Position.X,
                Y         = asteroid.Position.Y,
                Rotation  = asteroid.Rotation,
                Size      = asteroid.Size,
                HitPoints = asteroid.HitPoints,
            });
        }

        foreach (var proj in SelectProjectilesForSnapshot(recipientPlayerId))
        {
            snapshot.Projectiles.Add(new ProjectileSnapshot
            {
                Id      = proj.Id,
                X       = proj.Position.X,
                Y       = proj.Position.Y,
                OwnerId = proj.OwnerId,
            });
        }

        foreach (var powerUp in _powerUps.Values)
        {
            snapshot.PowerUps.Add(new PowerUpSnapshot
            {
                Id = powerUp.Id,
                X = powerUp.Position.X,
                Y = powerUp.Position.Y,
                Type = powerUp.Type,
            });
        }

        return snapshot;
    }

    private void BroadcastCountdown(int seconds)
    {
        BroadcastReliable(new CountdownPacket { SecondsRemaining = seconds });
    }

    private void BroadcastReliable(IPacket packet)
    {
        var writer = Serialize(packet);
        foreach (var peer in _peers.Values)
            peer.Send(writer, DeliveryMethod.ReliableOrdered);
    }

    private void BroadcastUnreliable(IPacket packet)
    {
        var writer = Serialize(packet);
        foreach (var peer in _peers.Values)
            peer.Send(writer, DeliveryMethod.Unreliable);
    }

    private static NetDataWriter Serialize(IPacket packet)
    {
        var nw = new NetDataWriter();
        nw.Put((byte)packet.Type);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        packet.Serialize(bw);
        bw.Flush();
        nw.Put(ms.ToArray());
        return nw;
    }

    private static void Send(NetPeer peer, IPacket packet, DeliveryMethod method)
    {
        var nw = Serialize(packet);
        peer.Send(nw, method);
    }

    // ──── INetEventListener ────────────────────────────────────────────────────

    public void OnConnectionRequest(ConnectionRequest request)
    {
        if (_peers.Count >= MaxPlayers)
        { request.Reject(); return; }
        request.AcceptIfKey(ConnectionKey);
    }

    public void OnPeerConnected(NetPeer peer)
    {
        _logger.LogInformation("Pair connecté : {EndPoint}", peer.Address);
        // L'inscription est finalisée à la réception de ConnectRequestPacket
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        if (!_peerPlayerIds.TryGetValue(peer, out var playerId)) return;

        _peerPlayerIds.Remove(peer);
        _peers.Remove(playerId);
        _latestInputs.TryRemove(playerId, out _);
        _playersReadyForLobby.Remove(playerId);
        if (_ships.TryGetValue(playerId, out var ship))
        {
            ship.IsAlive = false;
            _ships.Remove(playerId);
        }

        if (_phase == GamePhase.GameOver && _peers.Count > 0 &&
            _playersReadyForLobby.Count >= _peers.Count)
        {
            ResetMatchToLobby();
        }

        BroadcastLobbyState();
        _logger.LogInformation("Joueur {Id} déconnecté", playerId);
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader,
        byte channelNumber, DeliveryMethod method)
    {
        try
        {
            var type = (PacketType)reader.GetByte();
            var body = reader.GetRemainingBytes();

            using var ms = new MemoryStream(body);
            using var br = new BinaryReader(ms);

            switch (type)
            {
                case PacketType.ConnectRequest:
                    HandleConnectRequest(peer, br);
                    break;

                case PacketType.PlayerInput:
                    HandlePlayerInput(peer, br);
                    break;

                case PacketType.StartGameRequest:
                    HandleStartGameRequest(peer, br);
                    break;

                case PacketType.ReturnToLobbyRequest:
                    HandleReturnToLobbyRequest(peer, br);
                    break;

                case PacketType.LobbyStateRequest:
                    HandleLobbyStateRequest(peer, br);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erreur lors du traitement d'un paquet");
        }
    }

    private void HandleConnectRequest(NetPeer peer, BinaryReader reader)
    {
        var packet = new ConnectRequestPacket();
        packet.Deserialize(reader);

        var id   = _nextPlayerId++;
        var ship = new Ship
        {
            Id     = id,
            Pseudo = string.IsNullOrWhiteSpace(packet.Pseudo) ? $"Joueur{id}" : packet.Pseudo,
            Color  = packet.Color,
        };

        _ships[id]  = ship;
        _peers[id]  = peer;
        _peerPlayerIds[peer] = id;

        // Confirmation LobbyJoined
        Send(peer, new LobbyJoinedPacket { PlayerId = id, Message = "Bienvenue !" },
            DeliveryMethod.ReliableOrdered);

        BroadcastLobbyState();
        _logger.LogInformation("Joueur {Pseudo} (ID={Id}) rejoint le lobby", ship.Pseudo, id);
    }

    private void HandlePlayerInput(NetPeer peer, BinaryReader reader)
    {
        if (!_peerPlayerIds.TryGetValue(peer, out var playerId)) return;

        var packet = new PlayerInputPacket();
        packet.Deserialize(reader);
        _latestInputs.AddOrUpdate(
            playerId,
            packet,
            (_, existing) => packet.Timestamp >= existing.Timestamp ? packet : existing);
    }

    private void HandleStartGameRequest(NetPeer peer, BinaryReader reader)
    {
        _ = reader;
        if (_phase != GamePhase.Lobby)
            return;

        if (!_peerPlayerIds.TryGetValue(peer, out var playerId))
            return;

        var hostId = GetHostPlayerId();
        if (playerId != hostId)
        {
            _logger.LogWarning(
                "Demande StartGame refusée : joueur non hôte (Id={PlayerId}, Host={HostId})",
                playerId,
                hostId);
            return;
        }

        if (_ships.Count == 0)
            return;

        _logger.LogInformation("Démarrage demandé par l'hôte {HostId}", hostId);
        EnterCountdown();
    }

    private void HandleReturnToLobbyRequest(NetPeer peer, BinaryReader reader)
    {
        _ = reader;
        if (_phase != GamePhase.GameOver)
            return;

        if (!_peerPlayerIds.TryGetValue(peer, out var playerId))
            return;

        _playersReadyForLobby.Add(playerId);
        _logger.LogInformation(
            "Retour lobby confirmé par joueur {Id} ({Ready}/{Total})",
            playerId, _playersReadyForLobby.Count, _peers.Count);

        var hostId = GetHostPlayerId();
        if (playerId == hostId)
        {
            ResetMatchToLobby();
            return;
        }

        if (_peers.Count > 0 && _playersReadyForLobby.Count >= _peers.Count)
            ResetMatchToLobby();
    }

    private void HandleLobbyStateRequest(NetPeer peer, BinaryReader reader)
    {
        _ = reader;
        if (!_peerPlayerIds.ContainsKey(peer))
            return;

        // Renvoie un état lobby à jour au client demandeur.
        // Un broadcast est acceptable ici vu la petite taille du lobby.
        BroadcastLobbyState();
    }

    private void BroadcastLobbyState()
    {
        var lobbyPacket = new LobbyStatePacket();
        var hostId = GetHostPlayerId();
        lobbyPacket.HostPlayerId = hostId;
        foreach (var ship in _ships.Values)
            lobbyPacket.Players.Add(new LobbyPlayerInfo
            {
                Id     = ship.Id,
                Pseudo = ship.Pseudo,
                Color  = ship.Color,
                IsHost = ship.Id == hostId,
            });
        BroadcastReliable(lobbyPacket);
    }

    public void OnNetworkError(IPEndPoint endPoint, SocketError error)
        => _logger.LogWarning("Erreur réseau depuis {EP} : {Error}", endPoint, error);

    public void OnNetworkReceiveUnconnected(IPEndPoint ep, NetPacketReader r,
        UnconnectedMessageType t) { }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

    // ──── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        _cts.Cancel();
        _netManager.Stop();
    }

    private static int GetAsteroidScore(AsteroidSize size) => size switch
    {
        AsteroidSize.Large  => 120,
        AsteroidSize.Medium => 80,
        AsteroidSize.Small  => 50,
        _                   => 50,
    };

    private int GetHostPlayerId()
        => _ships.Count == 0 ? -1 : _ships.Keys.Min();

    private void EnterCountdown()
    {
        if (_phase != GamePhase.Lobby)
            return;

        _phase = GamePhase.Countdown;
        _countdownRemaining = CountdownSeconds;
        _countdownTimer = 0f;
        BroadcastCountdown(_countdownRemaining);
    }

    private void EnterGameOver(Ship? winner, string winnerNameFallback)
    {
        _phase = GamePhase.GameOver;
        _playersReadyForLobby.Clear();
        _gameOverElapsed = 0f;

        var resolvedWinnerName = winner?.Pseudo;
        if (string.IsNullOrWhiteSpace(resolvedWinnerName))
            resolvedWinnerName = string.IsNullOrWhiteSpace(winnerNameFallback)
                ? "Aucun survivant"
                : winnerNameFallback;

        var packet = new GameOverPacket
        {
            WinnerId = winner?.Id ?? -1,
            WinnerName = resolvedWinnerName,
            IsSoloMode = _currentMatchPlayerCount <= 1,
        };
        BroadcastReliable(packet);

        _logger.LogInformation("Fin de partie — Vainqueur : {Name}", packet.WinnerName);
    }

    private void TickGameOver(float dt)
    {
        _gameOverElapsed += dt;
        // Si tous les clients quittent pendant l'écran de fin, on remet l'état serveur au lobby.
        if (_peers.Count == 0)
        {
            ResetMatchToLobby();
            return;
        }

        // Protection anti-blocage si un client ne renvoie jamais ReturnToLobby.
        if (_gameOverElapsed >= GameOverAutoReturnDelaySeconds)
            ResetMatchToLobby();
    }

    private void ResetMatchToLobby()
    {
        _phase = GamePhase.Lobby;
        _countdownRemaining = CountdownSeconds;
        _countdownTimer = 0f;
        _snapshotAccumulator = 0f;
        _gameOverElapsed = 0f;
        _currentMatchPlayerCount = 0;
        _latestInputs.Clear();
        _playersReadyForLobby.Clear();

        _asteroids.Clear();
        _projectiles.Clear();
        _powerUps.Clear();
        _waveManager.Reset();
        _nextProjectileId = 1;
        _nextPowerUpId = 5000;
        _asteroidSvc.Reset();

        foreach (var ship in _ships.Values)
        {
            ship.IsAlive = true;
            ship.Velocity = System.Numerics.Vector2.Zero;
            ship.DashCooldown = 0f;
            ship.IsDashing = false;
            ship.DashTimeRemaining = 0f;
            ship.WeaponCooldown = 0f;
            ship.LaserCharges = 0;
            ship.LaserRemaining = 0f;
            ship.LivesRemaining = StartingLives;
            ship.InvulnerabilityRemaining = 0f;
        }

        BroadcastLobbyState();
        _logger.LogInformation("Session réinitialisée, retour au lobby");
    }
    private IEnumerable<Asteroid> SelectAsteroidsForSnapshot(int recipientPlayerId)
    {
        if (_asteroids.Count <= SnapshotAsteroidLimit)
            return _asteroids.Values;

        if (_ships.TryGetValue(recipientPlayerId, out var recipientShip) && recipientShip.IsAlive)
        {
            return _asteroids.Values
                .OrderBy(asteroid => SquaredWrappedDistance(asteroid.Position, recipientShip.Position))
                .Take(SnapshotAsteroidLimit)
                .ToArray();
        }

        var aliveShips = _ships.Values.Where(ship => ship.IsAlive).ToArray();
        if (aliveShips.Length == 0)
            return _asteroids.Values.Take(SnapshotAsteroidLimit).ToArray();

        return _asteroids.Values
            .OrderBy(asteroid => aliveShips.Min(ship => SquaredWrappedDistance(asteroid.Position, ship.Position)))
            .Take(SnapshotAsteroidLimit)
            .ToArray();
    }

    private IEnumerable<Projectile> SelectProjectilesForSnapshot(int recipientPlayerId)
    {
        if (_projectiles.Count <= SnapshotProjectileLimit)
            return _projectiles.Values;

        if (_ships.TryGetValue(recipientPlayerId, out var recipientShip) && recipientShip.IsAlive)
        {
            return _projectiles.Values
                .OrderBy(projectile => SquaredWrappedDistance(projectile.Position, recipientShip.Position))
                .Take(SnapshotProjectileLimit)
                .ToArray();
        }

        var aliveShips = _ships.Values.Where(ship => ship.IsAlive).ToArray();
        if (aliveShips.Length == 0)
            return _projectiles.Values.Take(SnapshotProjectileLimit).ToArray();

        return _projectiles.Values
            .OrderBy(projectile => aliveShips.Min(ship => SquaredWrappedDistance(projectile.Position, ship.Position)))
            .Take(SnapshotProjectileLimit)
            .ToArray();
    }

    private float SquaredWrappedDistance(System.Numerics.Vector2 a, System.Numerics.Vector2 b)
    {
        var dx = MathF.Abs(a.X - b.X);
        var dy = MathF.Abs(a.Y - b.Y);

        dx = MathF.Min(dx, _bounds.Width - dx);
        dy = MathF.Min(dy, _bounds.Height - dy);
        return (dx * dx) + (dy * dy);
    }

    private bool LaserIntersectsAsteroid(Vector2 start, Vector2 end, Vector2 center, float radius)
    {
        for (var offsetX = -1; offsetX <= 1; offsetX++)
        {
            for (var offsetY = -1; offsetY <= 1; offsetY++)
            {
                var shiftedCenter = center + new Vector2(offsetX * _bounds.Width, offsetY * _bounds.Height);
                if (SegmentIntersectsCircle(start, end, shiftedCenter, radius))
                    return true;
            }
        }

        return false;
    }

    private static bool SegmentIntersectsCircle(Vector2 start, Vector2 end, Vector2 center, float radius)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared <= float.Epsilon)
            return Vector2.DistanceSquared(start, center) <= radius * radius;

        var t = Vector2.Dot(center - start, segment) / lengthSquared;
        t = Math.Clamp(t, 0f, 1f);
        var closest = start + (segment * t);
        return Vector2.DistanceSquared(closest, center) <= radius * radius;
    }
}

