namespace AsteroidOnline.Infrastructure.Networking;

using System.Net;
using System.Net.Sockets;
using AsteroidOnline.GameLogic.Interfaces;
using AsteroidOnline.Shared.Packets;
using LiteNetLib;
using LiteNetLib.Utils;

/// <summary>
/// Implémentation du service réseau client basée sur LiteNetLib.
/// Encapsule un <see cref="NetManager"/> unique qui communique en UDP avec le serveur.
/// LiteNetLib fournit deux canaux de livraison :
/// <list type="bullet">
///   <item><see cref="DeliveryMethod.ReliableOrdered"/> — équivalent TCP (événements de jeu)</item>
///   <item><see cref="DeliveryMethod.Unreliable"/> — UDP brut (inputs temps-réel)</item>
/// </list>
/// </summary>
public sealed class LiteNetClientService : INetworkClientService, INetEventListener, IDisposable
{
    // Clé de connexion partagée entre client et serveur pour authentifier les pairs.
    private const string ConnectionKey = "AsteroidOnline_v1";

    private readonly NetManager _netManager;

    // Référence au pair serveur, initialisée lors de la connexion.
    private NetPeer? _serverPeer;

    // Source de complétion utilisée pour transformer la connexion async LiteNetLib
    // en une Task<bool> attendable par les ViewModels.
    private TaskCompletionSource<bool>? _connectTcs;

    /// <inheritdoc/>
    public bool IsConnected => _serverPeer?.ConnectionState == ConnectionState.Connected;

    /// <inheritdoc/>
    public int LatencyMs => _serverPeer?.RoundTripTime ?? 0;

    /// <inheritdoc/>
    public event Action<PacketType, BinaryReader>? PacketReceived;

    /// <inheritdoc/>
    public event Action? Disconnected;

    /// <summary>
    /// Initialise le service en démarrant le <see cref="NetManager"/> interne.
    /// </summary>
    public LiteNetClientService()
    {
        _netManager = new NetManager(this)
        {
            // Recyclage automatique des lecteurs de paquets après usage.
            AutoRecycle = true,
        };
        _netManager.Start();
    }

    // ──── INetworkClientService ───────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> ConnectAsync(string address, int port,
        CancellationToken cancellationToken = default)
    {
        _connectTcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            _serverPeer = _netManager.Connect(address, port, ConnectionKey);
            if (_serverPeer is null)
                return false;

            // Annulation du token → échec de connexion
            await using var _ = cancellationToken.Register(
                () => _connectTcs.TrySetResult(false));

            // Boucle de polling jusqu'à confirmation ou timeout de 10 s
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            while (!_connectTcs.Task.IsCompleted)
            {
                _netManager.PollEvents();

                try
                {
                    await Task.Delay(15, linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _connectTcs.TrySetResult(false);
                    break;
                }
            }

            return await _connectTcs.Task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            _connectTcs.TrySetResult(false);
            return false;
        }
    }

    /// <inheritdoc/>
    public void SendReliable(IPacket packet)
    {
        if (_serverPeer is null || !IsConnected)
            return;

        var writer = SerializePacket(packet);
        _serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
    }

    /// <inheritdoc/>
    public void SendUnreliable(IPacket packet)
    {
        if (_serverPeer is null || !IsConnected)
            return;

        var writer = SerializePacket(packet);
        _serverPeer.Send(writer, DeliveryMethod.Unreliable);
    }

    /// <inheritdoc/>
    public void PollEvents() => _netManager.PollEvents();

    /// <inheritdoc/>
    public void Disconnect()
    {
        _serverPeer?.Disconnect();
        _serverPeer = null;
    }

    // ──── INetEventListener ───────────────────────────────────────────────────

    /// <summary>Appelé par LiteNetLib à l'établissement de la connexion.</summary>
    public void OnPeerConnected(NetPeer peer)
    {
        _serverPeer = peer;
        _connectTcs?.TrySetResult(true);
    }

    /// <summary>Appelé par LiteNetLib lors d'une déconnexion.</summary>
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        _serverPeer = null;
        _connectTcs?.TrySetResult(false);
        Disconnected?.Invoke();
    }

    /// <summary>Appelé par LiteNetLib à la réception d'un paquet.</summary>
    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader,
        byte channelNumber, DeliveryMethod deliveryMethod)
    {
        // Le premier octet est le type du paquet.
        var packetType = (PacketType)reader.GetByte();
        var body       = reader.GetRemainingBytes();

        // On expose le corps dans un BinaryReader pour la désérialisation côté abonné.
        using var ms = new MemoryStream(body);
        using var br = new BinaryReader(ms);
        PacketReceived?.Invoke(packetType, br);
    }

    /// <summary>Appelé en cas d'erreur de socket.</summary>
    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        _connectTcs?.TrySetResult(false);
    }

    /// <summary>Non utilisé côté client (messages non connectés).</summary>
    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint,
        NetPacketReader reader, UnconnectedMessageType messageType) { }

    /// <summary>Mise à jour de la latence — mise à jour automatique de RoundTripTime.</summary>
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

    /// <summary>Demandes de connexion entrantes rejetées (le client n'accepte pas de pairs).</summary>
    public void OnConnectionRequest(ConnectionRequest request) => request.Reject();

    // ──── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sérialise un paquet dans un <see cref="NetDataWriter"/> prêt à être envoyé.
    /// Format : [1 octet : PacketType] [N octets : corps]
    /// </summary>
    private static NetDataWriter SerializePacket(IPacket packet)
    {
        var netWriter = new NetDataWriter();
        netWriter.Put((byte)packet.Type);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        packet.Serialize(bw);
        bw.Flush();

        netWriter.Put(ms.ToArray());
        return netWriter;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _netManager.Stop();
    }
}
