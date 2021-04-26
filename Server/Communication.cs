using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Fleck;

using Unknown6656;
using Unknown6656.Common;
using Unknown6656.IO;

namespace SKHEIJO
{
    public sealed class Box<T>
        where T : class
    {
        public T? Value;


        public static implicit operator Box<T>(T? value) => new Box<T> { Value = value };

        public static implicit operator T?(Box<T>? box) => box?.Value;
    }

    public record Player(Guid UUID)
    {
        public Union<TcpClient, WebSocketConnection>? Client { get; init; }


        public override string ToString()
        {
            string remote = Client?.Match(
                tcp => tcp.Client?.RemoteEndPoint is IPEndPoint ep ? $"(tcp) {ep}" : "tcp not connected",
                web => $"(web) {web.ConnectionInfo.ClientIpAddress}:{web.ConnectionInfo.ClientPort}"
            ) ?? "not connected";

            return $"{UUID} {remote}";
        }
    }

    public sealed class ConnectionString
    {
        private const string SCRAMBLER = "Vxawrn2v/IF4E+bYO5ueLd9y3WsCARmTXDpNKGBZhtkcPM=0zjQfoU7S6qJH1gi8l";

        public (ushort CSharp, ushort Web) Ports { get; }
        public IPAddress Address { get; }
        public bool IsIPv6 { get; }
        public IPEndPoint CSharpEndPoint { get; }


        public ConnectionString(string address, ushort port_csharp, ushort port_web)
            : this(IPAddress.Parse(address), port_csharp, port_web)
        {
        }

        public ConnectionString(IPAddress address, ushort port_csharp, ushort port_web)
            : this(address, (port_csharp, port_web))
        {
        }

        public ConnectionString(string address, (ushort csharp, ushort web) ports)
            : this(IPAddress.Parse(address), ports)
        {
        }

        public ConnectionString(IPAddress address, (ushort csharp, ushort web) ports)
        {
            Address = address;
            Ports = ports;
            CSharpEndPoint = new(Address, Ports.CSharp);
            IsIPv6 = address.AddressFamily is AddressFamily.InterNetworkV6;
        }

        public override string ToString()
        {
            char[] b64 = From.ArrayOfSources(
                From.Unmanaged(IsIPv6),
                From.String(Address.ToString()),
                From.Unmanaged(Ports)
            ).ToBase64().ToCharArray();

            for (int i = 0; i < b64.Length; ++i)
                b64[i] = SCRAMBLER[(SCRAMBLER.IndexOf(b64[i]) + i) % SCRAMBLER.Length];

            return new string(b64);
        }

        public override int GetHashCode() => ToString().GetHashCode();

        public override bool Equals(object? obj) => obj is ConnectionString cs && cs.ToString() == ToString();

        public static async Task<ConnectionString> GetMyConnectionString(ushort port_csharp, ushort port_web) =>
            await GetMyConnectionString((port_csharp, port_web));

        public static async Task<ConnectionString> GetMyConnectionString((ushort csharp, ushort web) ports)
        {
            using HttpClient client = new();
            string html = await client.GetStringAsync("https://api64.ipify.org/");
            IPAddress ip = IPAddress.Parse(html);

            return new(ip, ports);
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
                parts[2].ToUnmanaged<(ushort, ushort)>()
            );
        }

        public static implicit operator string(ConnectionString c) => c.ToString();

        public static implicit operator ConnectionString(string s) => FromString(s);
    }

    public interface ICommunicationData { }







    public readonly struct RawCommunicationPacket
    {
        private record internal_data(string Type, string? FullType, Guid Conversation, ICommunicationData Data);

        public readonly Guid ConversationIdentifier;
        public readonly byte[] MessageBytes;


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

        public ICommunicationData? DeserializeData()
        {
            string json = Encoding.UTF8.GetString(MessageBytes);

            if (JsonSerializer.Deserialize<internal_data>(json) is internal_data data)
            {
                Type type = Type.GetType(data.FullType ?? data.Type);

                // TODO

                return data.Data;
            }
            else
                return null;
        }

        public static RawCommunicationPacket SerializeData(Guid conversation, ICommunicationData data)
        {
            Type type = data.GetType();
            string json = JsonSerializer.Serialize(new internal_data(type.Name, type.AssemblyQualifiedName, conversation, data));

            return new(conversation, Encoding.UTF8.GetBytes(json));
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

    public delegate ICommunicationData? IncomingDataDelegate(Player player, ICommunicationData? message, bool reply_requested);

    public sealed class GameServer
        : IDisposable
    {
        public ConnectionString ConnectionString { get; }
        public TcpListener TCPListener { get; }
        public WebSocketServer WebSocketServer { get; }
        public bool IsRunning => _running != 0;
        public string ServerName { get; }

        private readonly ConcurrentDictionary<Player, ConcurrentQueue<RawCommunicationPacket>> _players;
        private readonly ConcurrentQueue<(Player, RawCommunicationPacket)> _incoming;
        private volatile int _running;


        public event IncomingDataDelegate? OnIncomingData;
        public event Action<Player>? OnPlayerJoined;
        public event Action<Player>? OnPlayerLeft;


        private GameServer(ConnectionString connection_string, string server_name)
        {
            ServerName = server_name;
            ConnectionString = connection_string;
            TCPListener = new(new IPEndPoint(connection_string.IsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, connection_string.Ports.CSharp));
            WebSocketServer = new($"ws://{(connection_string.IsIPv6 ? "[::]" : "0.0.0.0")}:{connection_string.Ports.Web}", true);
            WebSocketServer.ListenerSocket.NoDelay = true;
            WebSocketServer.RestartAfterListenError = true;
            _players = new();
            _incoming = new();
        }

        public void Start()
        {
            if (Interlocked.Exchange(ref _running, 1) == 0)
            {
                TCPListener.Start();
                WebSocketServer.Start(c => OnWebConnectionOpened((WebSocketConnection)c));

                Task.Factory.StartNew(async delegate
                {
                    while (_running != 0)
                        if (TCPListener.Pending())
                            try
                            {
                                await OnTCPClientConnected(await TCPListener.AcceptTcpClientAsync()).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                ex.Err(LogSource.Server);
                            }
                        else
                            await Task.Delay(5);
                });
                Task.Factory.StartNew(ProcessIncomingMessages);
            }
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _running, 0) != 0)
            {
                TCPListener.Stop();

                foreach ((Player player, ConcurrentQueue<RawCommunicationPacket> outgoing) in _players)
                {
                    outgoing.Clear();
                    player.Client?.Match(
                        tcp =>
                        {
                            tcp.Close();
                            tcp.Dispose();
                        },
                        web => web.Close()
                    );
                }

                _players.Clear();
            }
        }

        public void Dispose() => Stop();

        private ConcurrentQueue<RawCommunicationPacket> AddPlayer(Player player)
        {
            ConcurrentQueue<RawCommunicationPacket> outgoing = _players[player] = new();

            $"{player} connected.".Info(LogSource.Server);

            OnPlayerJoined?.Invoke(player);

            return outgoing;
        }

        private void RemovePlayer(Player player)
        {
            _players.TryRemove(player, out _);

            $"{player} disconnected.".Warn(LogSource.Server);

            OnPlayerLeft?.Invoke(player);
        }

        private async Task OnTCPClientConnected(TcpClient client)
        {
            Player? player = null;

            try
            {
                $"Incoming connection from {client.Client.RemoteEndPoint}.".Log(LogSource.Server);

                using (NetworkStream stream = client.GetStream())
                using (BinaryReader reader = new(stream))
                using (BinaryWriter writer = new(stream))
                {
                    Guid uuid = reader.ReadNative<Guid>();
                    ConcurrentQueue<RawCommunicationPacket> outgoing = AddPlayer(player = new(uuid) { Client = client });

                    writer.Write(ServerName);

                    ArraySegment<byte> keepalive_buffer = new byte[1];

                    while (_running != 0)
                        if (client.Client.Poll(0, SelectMode.SelectRead) && await client.Client.ReceiveAsync(keepalive_buffer, SocketFlags.Peek) == 0)
                        {
                            $"Connection to {player} lost.".Err(LogSource.Server);

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

                                    $"{packet} sent to {player}.".Log(LogSource.Server);
                                }
                                catch (Exception ex)
                                {
                                    ex.Warn(LogSource.Server);
                                }

                            while (stream.DataAvailable)
                                try
                                {
                                    RawCommunicationPacket packet = RawCommunicationPacket.ReadFrom(reader);

                                    _incoming.Enqueue((player, packet));
                                    idle = false;

                                    $"{packet} received from {player}.".Log(LogSource.Server);
                                }
                                catch (Exception ex)
                                {
                                    ex.Warn(LogSource.Server);
                                }

                            if (idle)
                                await Task.Delay(5);
                        }
                }

                client.Close();
            }
            finally
            {
                if (player is { })
                    RemovePlayer(player);

                client.Dispose();
            }
        }

        private async void OnWebConnectionOpened(WebSocketConnection socket)
        {
            Guid? guid = null;

            socket.OnBinary = bytes =>
            {
                unsafe
                {
                    if (bytes.Length >= sizeof(Guid))
                        guid = Guid.ParseExact(From.Bytes(bytes).ToHexString(), "N");
                }
            };

            while (_running != 0 && socket.IsAvailable && guid is null)
                await Task.Delay(20);

            if (guid.HasValue)
            {
                Player player = new(guid.Value)
                {
                    Client = socket
                };

                AddPlayer(player);
                Notify(player, );

                socket.OnClose = () => RemovePlayer(player);
                socket.OnBinary = bytes => OnWebsocketMessage(socket, player, bytes);
                socket.OnMessage = message => OnWebsocketMessage(socket, player, Encoding.UTF8.GetBytes(message));

                while (_running != 0 && socket.IsAvailable)
                    await Task.Delay(20);
            }

            socket.Close();
        }

        private async Task ProcessIncomingMessages()
        {
            while (_running != 0)
            {
                bool any = false;

                while (_incoming.TryDequeue(out var item))
                    try
                    {
                        any = true;

                        ICommunicationData? message = item.Item2.DeserializeData();
                        ICommunicationData? reply = OnIncomingData?.Invoke(item.Item1, message, item.Item2.ConversationIdentifier != Guid.Empty);

                        if (reply is { } && _players.TryGetValue(item.Item1, out ConcurrentQueue<RawCommunicationPacket>? outgoing))
                            outgoing.Enqueue(RawCommunicationPacket.SerializeData(item.Item2.ConversationIdentifier, reply));
                    }
                    catch (Exception ex)
                    {
                        ex.Warn(LogSource.Server);
                    }

                if (!any)
                    await Task.Delay(1);
            }
        }

        private void OnWebsocketMessage(WebSocketConnection socket, Player player, byte[] message)
        {
            $"Received {message.Length} bytes from '{player}'.".Log(LogSource.WebServer);

            From.Bytes(message).HexDump();


        }

        public bool Notify(Player player, ICommunicationData data)
        {
            bool res;

            if (res = _players.TryGetValue(player, out ConcurrentQueue<RawCommunicationPacket>? outgoing))
                outgoing?.Enqueue(RawCommunicationPacket.SerializeData(Guid.Empty, data));

            return res;
        }

        public void Notify(IEnumerable<Player> players, ICommunicationData data) => players.Do(p => Notify(p, data));

        public void NotifyAll(ICommunicationData data) => Notify(_players.Keys, data);

        public static GameServer CreateLocalGameServer(string addr, ushort port_csharp, ushort port_web, string name) => new(new(addr, port_csharp, port_web), name);

        public static async Task<GameServer> CreateGameServer(ushort port_csharp, ushort port_web, string name) =>
            new(await ConnectionString.GetMyConnectionString(port_csharp, port_web), name);
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


        public event Action<ICommunicationData?>? OnIncomingData;


        public GameClient(Guid uuid, ConnectionString connection_string)
        {
            ConnectionString = connection_string;
            _open_conversations = new();
            _outgoing = new();
            _incoming = new();

            TcpClient client = new();
            client.Connect(ConnectionString.CSharpEndPoint);

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

            $"Connected to '{ServerName}' via {ConnectionString.CSharpEndPoint}.".Info(LogSource.Client);
        }

        private async Task CommunicationHandler()
        {
            ArraySegment<byte> keepalive_buffer = new byte[1];
            Socket? client = Player.Client?.AsCase0?.Client;

            while (IsAlive)
                if (client is null || (client.Poll(0, SelectMode.SelectRead) && await client.ReceiveAsync(keepalive_buffer, SocketFlags.Peek) == 0))
                {
                    "Connection to server lost.".Err(LogSource.Client);

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

                        $"{packet} sent to server.".Log(LogSource.Client);
                    }

                    while (Stream.DataAvailable)
                    {
                        RawCommunicationPacket packet = RawCommunicationPacket.ReadFrom(Reader);
                        idle = false;

                        $"{packet} received from server.".Log(LogSource.Client);

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

        public void SendMessage(ICommunicationData data) => _outgoing.Enqueue(RawCommunicationPacket.SerializeData(Guid.Empty, data));

        public async Task<ICommunicationData?> SendMessageAndWaitForReply(ICommunicationData data)
        {
            RawCommunicationPacket? reply = null;
            Guid guid = Guid.NewGuid();

            _outgoing.Enqueue(RawCommunicationPacket.SerializeData(guid, data));

            while (reply is null)
                while (!_open_conversations.TryGetValue(guid, out reply))
                    await Task.Delay(1);

            return reply.Value.DeserializeData();
        }

        private async Task IncomingHandler()
        {
            while (IsAlive)
            {
                bool idle = true;

                while (_incoming.TryDequeue(out RawCommunicationPacket data))
                {
                    idle = false;
                    OnIncomingData?.Invoke(data.DeserializeData());
                }

                if (idle)
                    await Task.Yield();
            }
        }

        public void Dispose()
        {
            IsAlive = false;

            Reader.Close();
            Reader.Dispose();
            Writer.Close();
            Writer.Dispose();
            Stream.Close();
            Stream.Dispose();

            if (Player.Client?.AsCase0?.Client is Socket client)
            {
                client.Close();
                client.Dispose();
            }
        }
    }
}
