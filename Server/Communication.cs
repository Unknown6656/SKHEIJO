using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Unknown6656.IO;
using Unknown6656.Mathematics;

namespace SKHEIJO
{
    public record Player(Guid UUID)
    {
        public TcpClient? Client { get; init; }


        public override string ToString() => $"{UUID} ({(Client?.Client?.RemoteEndPoint is IPEndPoint ep ? ep.ToString() : "not connected")})";
    }

    public sealed class ConnectionString
    {
        private const string SCRAMBLER = "Vxawrn2v/IF4E+bYO5ueLd9y3WsCARmTXDpNKGBZhtkcPM=0zjQfoU7S6qJH1gi8l";
 
        public ushort Port { get; }
        public IPAddress Address { get; }
        public bool IsIPv6 { get; }
        public IPEndPoint EndPoint { get; }


        public ConnectionString(IPEndPoint end_point)
            : this(end_point.Address, (ushort)end_point.Port)
        {
        }

        public ConnectionString(string address, ushort port)
            : this(new(IPAddress.Parse(address), port))
        {
        }

        public ConnectionString(IPAddress address, ushort port)
        {
            Address = address;
            Port = port;
            EndPoint = new(Address, Port);
            IsIPv6 = address.AddressFamily is AddressFamily.InterNetworkV6;
        }

        public override string ToString()
        {
            char[] b64 = From.ArrayOfSources(
                From.Unmanaged(IsIPv6),
                From.String(Address.ToString()),
                From.Unmanaged(Port)
            ).ToBase64().ToCharArray();

            for (int i = 0; i < b64.Length; ++i)
                b64[i] = SCRAMBLER[(SCRAMBLER.IndexOf(b64[i]) + i) % SCRAMBLER.Length];

            return new string(b64);
        }

        public override int GetHashCode() => ToString().GetHashCode();

        public override bool Equals(object? obj) => obj is ConnectionString cs && cs.ToString() == ToString();

        public static async Task<ConnectionString> GetMyConnectionString(ushort port)
        {
            using HttpClient client = new();
            string html = await client.GetStringAsync("https://api64.ipify.org/");
            IPAddress ip = IPAddress.Parse(html);

            return new(ip, port);
        }

        public static ConnectionString FromString(string connection_string)
        {
            char[] input = connection_string.ToCharArray();
            int len = SCRAMBLER.Length;

            for (int i = 0; i < input.Length; ++i)
                input[i] = SCRAMBLER[((SCRAMBLER.IndexOf(input[i]) - i) % len + len) % len];

            From[] parts = From.Base64(new(input)).ToArrayOfSources();

            return new(
                IPAddress.Parse(parts[1].ToString()),
                parts[2].ToUnmanaged<ushort>()
            );
        }

        public static implicit operator string(ConnectionString c) => c.ToString();

        public static implicit operator IPEndPoint(ConnectionString c) => c.EndPoint;

        public static implicit operator ConnectionString(IPEndPoint ep) => new(ep);

        public static implicit operator ConnectionString(string s) => FromString(s);
    }

    public readonly struct RawCommunicationPacket
    {
        public readonly Guid ConversationIdentifier;
        public readonly byte[] MessageBytes;


        public RawCommunicationPacket(byte[] bytes)
            : this(Guid.NewGuid(), bytes)
        {
        }

        public RawCommunicationPacket(Guid conversation, byte[] bytes)
        {
            ConversationIdentifier = conversation;
            MessageBytes = bytes;
        }

        public override string ToString() => $"{ConversationIdentifier}: {MessageBytes.Length} Bytes";

        public void WriteTo(BinaryWriter writer)
        {
            bool compressed = MessageBytes.Length > 128;

            writer.WriteNative(ConversationIdentifier);
            writer.WriteNative(compressed);
            writer.WriteCollection(compressed ? MessageBytes.Compress(CompressionFunction.GZip) : MessageBytes);
        }

        public static RawCommunicationPacket ReadFrom(BinaryReader reader)
        {
            Guid conversation_id = reader.ReadNative<Guid>();
            bool compressed = reader.ReadBoolean();
            byte[] bytes = reader.ReadCollection<byte>();

            if (compressed)
                bytes = bytes.Uncompress(CompressionFunction.GZip);

            return new(conversation_id, bytes);
        }
    }

    public delegate byte[]? IncomingDataDelegate(Player player, byte[] message, bool reply_requested);

    public sealed class GameServer
        : IDisposable
    {
        public ConnectionString ConnectionString { get; }
        public TcpListener Listener { get; }
        public bool IsRunning => _running != 0;
        public string ServerName { get; }

        private readonly ConcurrentDictionary<Player, ConcurrentQueue<RawCommunicationPacket>> _players;
        private readonly ConcurrentQueue<(Player, RawCommunicationPacket)> _incoming;
        private volatile int _running;


        public event IncomingDataDelegate? OnIncomingData;


        private GameServer(ConnectionString connection_string, string server_name)
        {
            ServerName = server_name;
            ConnectionString = connection_string;
            Listener = new(new IPEndPoint(connection_string.IsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, connection_string.Port));
            _players = new();
            _incoming = new();
        }

        public void Start()
        {
            if (Interlocked.Exchange(ref _running, 1) == 0)
            {
                Listener.Start();

                Task.Factory.StartNew(async delegate
                {
                    while (_running != 0)
                        await Handle(Listener).ConfigureAwait(false);
                });
                Task.Factory.StartNew(async delegate
                {
                    while (_running != 0)
                    {
                        bool any = false;

                        while (_incoming.TryDequeue(out var item))
                            try
                            {
                                any = true;

                                byte[]? reply = OnIncomingData?.Invoke(item.Item1, item.Item2.MessageBytes, item.Item2.ConversationIdentifier != Guid.Empty);

                                if (reply is { } && _players.TryGetValue(item.Item1, out ConcurrentQueue<RawCommunicationPacket>? outgoing))
                                    outgoing.Enqueue(new(item.Item2.ConversationIdentifier, reply));
                            }
                            catch (Exception ex)
                            {
                                ex.Warn();
                            }

                        if (!any)
                            await Task.Delay(2);
                    }
                });
            }
        }

        private async Task Handle(TcpListener listener)
        {
            if (listener.Pending())
                try
                {
                    await OnClientConnected(await listener.AcceptTcpClientAsync()).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ex.Err();
                }
        }

        private async Task OnClientConnected(TcpClient client)
        {
            Player? player = null;

            try
            {
                $"Incoming connection from {client.Client.RemoteEndPoint}.".Log();

                using (NetworkStream stream = client.GetStream())
                using (BinaryReader reader = new(stream))
                using (BinaryWriter writer = new(stream))
                {
                    Guid uuid = reader.ReadNative<Guid>();
                    ConcurrentQueue<RawCommunicationPacket> outgoing = new();

                    _players[player = new(uuid)
                    {
                        Client = client
                    }] = outgoing;

                    writer.Write(ServerName);

                    $"{player} connected.".Ok();

                    ArraySegment<byte> keepalive_buffer = new byte[1];

                    while (_running != 0)
                        if (client.Client.Poll(0, SelectMode.SelectRead) && await client.Client.ReceiveAsync(keepalive_buffer, SocketFlags.Peek) == 0)
                        {
                            $"Connection to {player} lost.".Err();

                            break;
                        }
                        else
                        {
                            bool idle = true;

                            while (outgoing.TryDequeue(out RawCommunicationPacket packet))
                                try
                                {
                                    idle = false;
                                    packet.WriteTo(writer);

                                    $"{packet} sent to {player}.".Log();
                                }
                                catch (Exception ex)
                                {
                                    ex.Warn();
                                }

                            while (stream.DataAvailable)
                                try
                                {
                                    RawCommunicationPacket packet = RawCommunicationPacket.ReadFrom(reader);

                                    _incoming.Enqueue((player, packet));
                                    idle = false;

                                    $"{packet} received from {player}.".Log();
                                }
                                catch (Exception ex)
                                {
                                    ex.Warn();
                                }

                            if (idle)
                                await Task.Delay(5);
                        }
                }

                client.Close();
            }
            finally
            {
                $"{player} disconnected.".Warn();

                if (player is { })
                    _players.TryRemove(player, out _);

                client.Dispose();
            }
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _running, 0) != 0)
            {
                Listener.Stop();

                foreach ((Player player, ConcurrentQueue<RawCommunicationPacket> outgoing) in _players)
                {
                    outgoing.Clear();
                    player.Client?.Close();
                    player.Client?.Dispose();
                }

                _players.Clear();
            }
        }

        public void Dispose() => Stop();

        public static GameServer CreateLocalGameServer(string addr, ushort port, string name) => new(new(addr, port), name);

        public static async Task<GameServer> CreateGameServer(ushort port, string name) => new(await ConnectionString.GetMyConnectionString(port), name);
    }

    public sealed class GameClient
        : IDisposable
    {
        public ConnectionString ConnectionString { get; }
        public NetworkStream Stream { get; }
        public BinaryWriter Writer { get; }
        public BinaryReader Reader { get; }
        public Player Player { get; }
        public string ServerName { get; }
        public bool IsAlive { get; private set; }

        private readonly ConcurrentDictionary<Guid, RawCommunicationPacket?> _open_conversations;
        private readonly ConcurrentQueue<RawCommunicationPacket> _outgoing;
        private readonly ConcurrentQueue<RawCommunicationPacket> _incoming;


        public event Action<byte[]>? OnIncomingData;


        public GameClient(Guid uuid, ConnectionString connection_string)
        {
            ConnectionString = connection_string;
            _open_conversations = new();
            _outgoing = new();
            _incoming = new();

            TcpClient client = new();
            client.Connect(ConnectionString.EndPoint);

            Stream = client.GetStream();
            Player = new(uuid)
            {
                Client = client
            };
            Writer = new(Stream);
            Reader = new(Stream);
            IsAlive = true;

            Writer.WriteNative(uuid);
            ServerName = Reader.ReadString();

            Task.Factory.StartNew(CommunicationHandler);
            Task.Factory.StartNew(IncomingHandler);

            $"Connected to '{ServerName}' via {ConnectionString.EndPoint}.".Ok();
        }

        private async Task CommunicationHandler()
        {
            ArraySegment<byte> keepalive_buffer = new byte[1];
            Socket? client = Player?.Client?.Client;

            while (IsAlive)
                if (client is null || (client.Poll(0, SelectMode.SelectRead) && await client.ReceiveAsync(keepalive_buffer, SocketFlags.Peek) == 0))
                {
                    "Connection to server lost.".Err();

                    break;
                }
                else
                {
                    bool idle = true;

                    while (_outgoing.TryDequeue(out RawCommunicationPacket packet))
                    {
                        idle = false;
                        packet.WriteTo(Writer);

                        if (packet.ConversationIdentifier != Guid.Empty)
                            _open_conversations[packet.ConversationIdentifier] = null;

                        $"{packet} sent to server.".Log();
                    }

                    while (Stream.DataAvailable)
                    {
                        RawCommunicationPacket packet = RawCommunicationPacket.ReadFrom(Reader);
                        idle = false;

                        $"{packet} received from server.".Log();

                        if (packet.ConversationIdentifier != Guid.Empty && _open_conversations.ContainsKey(packet.ConversationIdentifier))
                            _open_conversations[packet.ConversationIdentifier] = packet;
                        else
                            _incoming.Enqueue(packet);
                    }

                    if (idle)
                        await Task.Yield();
                }

            if (IsAlive)
                Dispose();
        }

        public void SendMessage(byte[] data) => _outgoing.Enqueue(new(Guid.Empty, data));

        public async Task<byte[]> SendMessageAndWaitForReply(byte[] data)
        {
            RawCommunicationPacket packet = new(data);
            RawCommunicationPacket? reply = null;

            _outgoing.Enqueue(packet);

            while (reply is null)
                while (!_open_conversations.TryGetValue(packet.ConversationIdentifier, out reply))
                    await Task.Yield();

            return reply.Value.MessageBytes;
        }

        private async Task IncomingHandler()
        {
            while (IsAlive)
            {
                bool idle = true;

                while (_incoming.TryDequeue(out RawCommunicationPacket data))
                {
                    idle = false;
                    OnIncomingData?.Invoke(data.MessageBytes);
                }

                if (idle)
                    await Task.Yield();
            }
        }

        public void Dispose()
        {
            IsAlive = false;

            Reader.Close();
            Writer.Close();
            Stream.Close();
            Player.Client?.Close();
            Player.Client?.Dispose();
        }
    }
}
