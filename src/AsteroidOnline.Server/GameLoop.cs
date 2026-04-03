namespace AsteroidOnline.Server;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
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
    private const int    MaxPlayers          = 16;
    private const int    CountdownSeconds    = 5;

    // ── Infrastructure réseau ──────────────────────────────────────────────────
    private readonly NetManager _netManager;
    private readonly ILogger<GameLoop> _logger;

    // ── État du monde ──────────────────────────────────────────────────────────
    private readonly WorldBounds       _bounds   = WorldBounds.Default;
    private readonly Dictionary<int, Ship>      _ships      = new();
    private readonly Dictionary<int, NetPeer>   _peers      = new();
    private readonly Dictionary<int, Asteroid>  _asteroids  = new();
    private readonly Dictionary<int, Projectile> _projectiles = new();

    // File thread-safe pour les inputs reçus depuis le thread réseau
    private readonly ConcurrentQueue<(int PlayerId, PlayerInputPacket Input)> _inputQueue = new();

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
    private int   _nextPlayerId = 1;
    private int   _nextProjectileId = 1;
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
        }
    }

    // ──── Phase Lobby ──────────────────────────────────────────────────────────

    private void TickLobby(float dt)
    {
        // Démarrage du compte à rebours dès qu'au moins 1 joueur est connecté
        if (_ships.Count >= 1)
        {
            _phase = GamePhase.Countdown;
            _countdownRemaining = CountdownSeconds;
            _countdownTimer = 0f;
            BroadcastCountdown(_countdownRemaining);
        }
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

        // Spawn des joueurs à des positions sûres
        var allEntities = new List<PhysicalEntity>(_ships.Values);
        foreach (var ship in _ships.Values)
        {
            ship.Position = _spawnSvc.FindSpawnPosition(allEntities);
            ship.IsAlive  = true;
            ship.Velocity = System.Numerics.Vector2.Zero;
            allEntities.Add(ship);
        }

        // Spawn des 10 astéroïdes initiaux (US-13)
        foreach (var asteroid in _asteroidSvc.SpawnInitialWave(10))
            _asteroids[asteroid.Id] = asteroid;

        _logger.LogInformation("Partie démarrée avec {Count} joueur(s)", _ships.Count);
    }

    // ──── Phase Playing ────────────────────────────────────────────────────────

    private void TickPlaying(float dt)
    {
        // 1. Traitement des inputs en file
        ProcessInputQueue();

        // 2. Physique des vaisseaux (via états d'input mémorisés dans le ship)
        foreach (var ship in _ships.Values)
        {
            if (!ship.IsAlive) continue;
            _dash.Tick(ship, false, dt);    // Gestion du cooldown dash
            _weapon.UpdateCooldown(ship, dt);
            _physics.Tick(ship, false, false, false, dt, in _bounds);
        }

        // 3. Physique des astéroïdes
        foreach (var asteroid in _asteroids.Values)
            _physics.Tick(asteroid, dt, in _bounds);

        // 4. Physique et durée de vie des projectiles
        var projectilesToRemove = new List<int>();
        foreach (var proj in _projectiles.Values)
        {
            proj.LifetimeRemaining -= dt;
            if (proj.LifetimeRemaining <= 0f)
            {
                proj.IsActive = false;
                projectilesToRemove.Add(proj.Id);
                continue;
            }
            _physics.Tick(proj, dt, in _bounds);
        }
        foreach (var id in projectilesToRemove)
            _projectiles.Remove(id);

        // 5. Détection de collisions
        ProcessCollisions();

        // 6. Vagues d'astéroïdes (US-16)
        if (_waveManager.Tick(dt, _asteroids.Count))
        {
            foreach (var asteroid in _asteroidSvc.SpawnWave(_asteroids.Count, WaveManager.MaxAsteroids))
                _asteroids[asteroid.Id] = asteroid;
            _logger.LogInformation("Vague {Wave} déclenchée", _waveManager.CurrentWave);
        }

        // 7. Broadcast snapshot toutes les 3 ticks (20 Hz)
        _snapshotAccumulator += dt;
        if (_snapshotAccumulator >= (TickDurationMs * SnapshotEveryNTicks / 1000f))
        {
            _snapshotAccumulator = 0f;
            BroadcastSnapshot();
        }

        // 8. Vérification fin de partie
        CheckGameOver();
    }

    // ──── Traitement des inputs ────────────────────────────────────────────────

    private void ProcessInputQueue()
    {
        while (_inputQueue.TryDequeue(out var item))
        {
            var (playerId, input) = item;
            if (!_ships.TryGetValue(playerId, out var ship) || !ship.IsAlive)
                continue;

            // Rotation et poussée via PhysicsSystem
            var dt = (float)(TickDurationMs / 1000.0);
            _dash.Tick(ship, input.Dash, dt);
            _physics.Tick(ship, input.ThrustForward, input.RotateLeft, input.RotateRight, dt, in _bounds);

            // Tir (US-09)
            var proj = _weapon.TryFire(ship, input.Fire, _nextProjectileId);
            if (proj != null)
            {
                _nextProjectileId++;
                _projectiles[proj.Id] = proj;
            }
        }
    }

    // ──── Collisions ───────────────────────────────────────────────────────────

    private void ProcessCollisions()
    {
        var ships = _ships.Values.ToList();

        // Projectile ↔ Astéroïde
        foreach (var proj in _projectiles.Values.ToList())
        {
            var hit = _collision.CheckProjectileVsAsteroids(proj, _asteroids.Values);
            if (hit is null) continue;

            proj.IsActive = false;
            _projectiles.Remove(proj.Id);
            DamageAsteroid(hit, proj.OwnerId);
        }

        // Projectile ↔ Joueur (US-22)
        foreach (var proj in _projectiles.Values.ToList())
        {
            var victim = _collision.CheckProjectileVsShips(proj, ships);
            if (victim is null) continue;

            proj.IsActive = false;
            _projectiles.Remove(proj.Id);
            EliminatePlayer(victim, proj.OwnerId);
        }

        // Astéroïde ↔ Joueur (US-15)
        foreach (var asteroid in _asteroids.Values)
        {
            var victim = _collision.CheckAsteroidVsShip(asteroid, ships);
            if (victim is null) continue;
            EliminatePlayer(victim, -1);
        }
    }

    private void DamageAsteroid(Asteroid asteroid, int shooterId)
    {
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

        // Score du tireur — comptabilisé en BLOC 5
        _ = shooterId;

        _logger.LogDebug("Astéroïde {Id} détruit", asteroid.Id);
    }

    private void EliminatePlayer(Ship victim, int killerId)
    {
        if (!victim.IsAlive) return;

        victim.IsAlive = false;
        _ships.TryGetValue(killerId, out var killer);

        var packet = new PlayerEliminatedPacket
        {
            VictimId   = victim.Id,
            VictimName = victim.Pseudo,
            KillerName = killer?.Pseudo ?? "Astéroïde",
        };
        BroadcastReliable(packet);

        _logger.LogInformation("{Victim} éliminé par {Killer}", victim.Pseudo,
            killer?.Pseudo ?? "un astéroïde");
    }

    private void CheckGameOver()
    {
        var alive = _ships.Values.Count(s => s.IsAlive);
        if (alive > 1 || _ships.Count <= 1) return;

        var winner = _ships.Values.FirstOrDefault(s => s.IsAlive);
        _phase = GamePhase.GameOver;

        BroadcastReliable(new GameOverPacket
        {
            WinnerId   = winner?.Id ?? -1,
            WinnerName = winner?.Pseudo ?? "Personne",
        });

        _logger.LogInformation("Fin de partie — Vainqueur : {Name}", winner?.Pseudo ?? "Personne");
    }

    // ──── Broadcast helpers ────────────────────────────────────────────────────

    private void BroadcastSnapshot()
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
            });
        }

        foreach (var asteroid in _asteroids.Values)
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

        foreach (var proj in _projectiles.Values)
        {
            snapshot.Projectiles.Add(new ProjectileSnapshot
            {
                Id      = proj.Id,
                X       = proj.Position.X,
                Y       = proj.Position.Y,
                OwnerId = proj.OwnerId,
            });
        }

        BroadcastUnreliable(snapshot);
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
        var entry = _peers.FirstOrDefault(kv => kv.Value == peer);
        if (entry.Value is null) return;

        _peers.Remove(entry.Key);
        if (_ships.TryGetValue(entry.Key, out var ship))
        {
            ship.IsAlive = false;
            _ships.Remove(entry.Key);
        }

        BroadcastLobbyState();
        _logger.LogInformation("Joueur {Id} déconnecté", entry.Key);
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

        // Confirmation LobbyJoined
        Send(peer, new LobbyJoinedPacket { PlayerId = id, Message = "Bienvenue !" },
            DeliveryMethod.ReliableOrdered);

        BroadcastLobbyState();
        _logger.LogInformation("Joueur {Pseudo} (ID={Id}) rejoint le lobby", ship.Pseudo, id);
    }

    private void HandlePlayerInput(NetPeer peer, BinaryReader reader)
    {
        var entry = _peers.FirstOrDefault(kv => kv.Value == peer);
        if (entry.Value is null) return;

        var packet = new PlayerInputPacket();
        packet.Deserialize(reader);
        _inputQueue.Enqueue((entry.Key, packet));
    }

    private void BroadcastLobbyState()
    {
        var lobbyPacket = new LobbyStatePacket();
        foreach (var ship in _ships.Values)
            lobbyPacket.Players.Add(new LobbyPlayerInfo
            {
                Id     = ship.Id,
                Pseudo = ship.Pseudo,
                Color  = ship.Color,
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
}
