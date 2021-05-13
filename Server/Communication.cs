using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Linq;
using System.Net;
using System.IO;
using System;

using Fleck;

using Unknown6656.Common;
using Unknown6656.IO;
using Unknown6656;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;

namespace SKHEIJO
{
    public record Player(Guid UUID)
    {
        public Union<TcpClient, WebSocketConnection>? Client { get; init; }


        public override string ToString()
        {
            string? remote = null;

            try
            {
                remote = Client?.Match(
                    tcp => $"(tcp) {tcp.Client?.RemoteEndPoint}",
                    web => $"(web) {web.ConnectionInfo.ClientIpAddress}:{web.ConnectionInfo.ClientPort}"
                );
            }
            catch
            {
            }

            return $"{UUID} {remote ?? "not connected"}";
        }
    }

    public sealed class ConnectionString
    {
        public (ushort CSharp, ushort WS, ushort WSS) Ports { get; }
        public string Address { get; }
        public bool IsIPv6 { get; }


        public ConnectionString(IPAddress address, ushort port_csharp, ushort port_ws, ushort port_wss)
            : this(address.ToString(), port_csharp, port_ws, port_wss, address.AddressFamily is AddressFamily.InterNetworkV6)
        {
        }

        public ConnectionString(string address, ushort port_csharp, ushort port_ws, ushort port_wss, bool is_v6 = false)
        {
            Address = address;
            Ports = (port_csharp, port_ws, port_wss);
            IsIPv6 = is_v6;
        }

        public ConnectionString With(IPAddress address) => new(address, Ports.CSharp, Ports.WS, Ports.WSS);

        public ConnectionString With(string address) => new(address, Ports.CSharp, Ports.WS, Ports.WSS);

        public override int GetHashCode() => ToString().GetHashCode();

        public override bool Equals(object? obj) => obj is ConnectionString cs && cs.ToString() == ToString();

        public override string ToString() => From.String($"{Address}${Ports.CSharp}${Ports.WS}${Ports.WSS}${(IsIPv6 ? 1 : 0)}").ToBase64();

        public static ConnectionString FromString(string connection_string)
        {
            string[] parts = From.Base64(connection_string).ToString().Split('$');

            return new(
                parts[0],
                ushort.Parse(parts[1]),
                ushort.Parse(parts[2]),
                ushort.Parse(parts[3]),
                parts[4] == "1"
            );
        }

        public static async Task<ConnectionString> GetMyConnectionString(ushort port_csharp, ushort port_ws, ushort port_wss)
        {
            using HttpClient client = new();
            string html = await client.GetStringAsync("https://api64.ipify.org/");
            IPAddress ip = IPAddress.Parse(html);

            return new(ip, port_csharp, port_ws, port_wss);
        }

        public static implicit operator string(ConnectionString c) => c.ToString();

        public static implicit operator ConnectionString(string s) => FromString(s);
    }

    public readonly struct RawCommunicationPacket
    {
        private record internal_data(string Type, string? FullType, Guid Conversation, object Data);

        public static Encoding Encoding { get; } = Encoding.UTF8;

        public readonly Guid ConversationIdentifier;
        public readonly Union<byte[], string> Message;


        public RawCommunicationPacket(Guid conversation, Union<byte[], string> message)
        {
            ConversationIdentifier = conversation;
            Message = message;
        }

        public override string ToString() => $"{ConversationIdentifier}: {Message.Match(b => $"{b.Length} bytes", s => $"{s.Length} UTF-8 chars")}";

        public void WriteTo(BinaryWriter writer)
        {
            byte[] bytes = Message.Match(LINQ.id, Encoding.GetBytes);
            bool compressed = bytes.Length > 128;

            writer.WriteNative(ConversationIdentifier);
            writer.WriteNative(compressed);
            writer.WriteCollection(compressed ? bytes.Compress(CompressionFunction.GZip) : bytes);
        }

        public CommunicationData? DeserializeData() => DeserializeData(Message)?.data;

        internal static (CommunicationData? data, Guid guid)? DeserializeData(Union<byte[], string> message)
        {
            try
            {
                if (message.Match(From.Bytes, From.String).ToJSON<internal_data>(Encoding) is internal_data { Type: string } data)
                {
                    if (!CommunicationData.KnownDerivativeTypes.TryGetValue(data.Type, out Type? type))
                        type = Type.GetType(data.FullType ?? data.Type);

                    if (type is { } && data.Data?.ToString() is string inner_json && JsonSerializer.Deserialize(inner_json, type) is CommunicationData comm)
                        return (comm, data.Conversation);
                }
            }
            catch (Exception ex)
            {
                ex.Err(LogSource.Unknown);
            }
            
            return null;
        }

        public static RawCommunicationPacket SerializeData(Guid conversation, CommunicationData data)
        {
            Type type = data.GetType();
            string json = From.JSON(new internal_data(type.Name, type.AssemblyQualifiedName, conversation, data)).ToString();

            return new(conversation, json);
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

    public delegate CommunicationData? IncomingDataDelegate(Player player, CommunicationData? message, bool reply_requested);

    public sealed class PlayerInfo
        : IDisposable
    {
        private string _name;
        private bool _is_admin;

        public Player Player { get; }
        public GameServer Server { get; }
        public Union<BinaryWriter, WebSocketConnection> Connection { get; }

        public string Name
        {
            get => _name;
            set
            {
                if (Interlocked.Exchange(ref _name, value) != value)
                    Server.NotifyAll(new CommunicationData_PlayerInfoChanged(Player.UUID));
            }
        }

        public bool IsAdmin
        {
            get => _is_admin;
            set
            {
                if (_is_admin != value)
                {
                    _is_admin = value;

                    Server.NotifyAll(new CommunicationData_PlayerInfoChanged(Player.UUID));
                }
            }
        }


        internal PlayerInfo(GameServer server, Player player, Union<BinaryWriter, WebSocketConnection> connection)
        {
            Server = server;
            Player = player;
            Connection = connection;
            _name = $"Player-{player.UUID.GetHashCode():x8}";
        }

        public override string ToString() => $"{Name}{(IsAdmin ? " (admin)" : "")}: {Player}";

        public void Dispose()
        {
            if (Connection.Is(out BinaryWriter? writer))
                writer?.Dispose();

            Player.Client?.Match(
                tcp =>
                {
                    tcp.Close();
                    tcp.Dispose();
                },
                web => web.Close()
            );
        }
    }

    public sealed class GameServer
        : IDisposable
    {
        internal readonly ConcurrentDictionary<Player, PlayerInfo> _players;
        private readonly ConcurrentQueue<(Player, RawCommunicationPacket)> _incoming;
        private readonly ConcurrentQueue<(Player, RawCommunicationPacket)> _outgoing;
        private volatile int _running;
        private volatile int _stopping;

        public HashSet<Guid>? AdminUUIDs { get; set; }
        public Game? CurrentGame { get; private set; }
        public ConnectionString ConnectionString { get; }
        public WebSocketServer WebSocketServer { get; }
        public WebSocketServer? WebSocketServerSSL { get; }
        public TcpListener TCPListener { get; }
        public HashSet<string> BannedNames { get; }
        public string ServerName { get; }
        public bool IsRunning => _running != 0;

        public PlayerInfo? this[Player player] => _players.TryGetValue(player, out PlayerInfo? info) ? info : null;

        public PlayerInfo? this[Guid guid] => _players.ToArray().SelectWhere(entry => entry.Key.UUID == guid, entry => entry.Value).FirstOrDefault();


        public event IncomingDataDelegate? OnIncomingData;
        public event Action<Player>? OnPlayerJoined;
        public event Action<Player>? OnPlayerLeft;


        private GameServer(ConnectionString connection_string, ServerConfig config)
        {
            ServerName = config.server_name;
            ConnectionString = connection_string;
            BannedNames = new(StringComparer.InvariantCultureIgnoreCase);
            CurrentGame = null;
            _players = new();
            _incoming = new();
            _outgoing = new();
            AdminUUIDs = new();
            TCPListener = new(new IPEndPoint(connection_string.IsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, connection_string.Ports.CSharp));
            WebSocketServer = new($"ws://{(connection_string.IsIPv6 ? "[::]" : "0.0.0.0")}:{connection_string.Ports.WS}", true);
            WebSocketServer.ListenerSocket.NoDelay = true;
            WebSocketServer.RestartAfterListenError = true;
            WebSocketServer.EnabledSslProtocols = SslProtocols.Ssl3 | SslProtocols.Ssl2 | SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls;

            if (config.certificate_path is string path)
            {
                WebSocketServerSSL = new($"wss://{(connection_string.IsIPv6 ? "[::]" : "0.0.0.0")}:{connection_string.Ports.WSS}", true);
                WebSocketServerSSL.ListenerSocket.NoDelay = WebSocketServer.ListenerSocket.NoDelay;
                WebSocketServerSSL.RestartAfterListenError = WebSocketServer.RestartAfterListenError;
                WebSocketServerSSL.EnabledSslProtocols = WebSocketServer.EnabledSslProtocols;
                WebSocketServerSSL.Certificate = new X509Certificate2(path, config.pfx_password);
            }

            AddBannedNames(config.banned_names);
            AdminUUIDs ??= new();

            config?.admin_uuids?.Do(u => AdminUUIDs.Add(u));
        }

        public void Start()
        {
            _stopping = 0;

            if (Interlocked.Exchange(ref _running, 1) == 0)
            {
                ResetNewGame();

                TCPListener.Start();
                WebSocketServer.Start(c => OnWebConnectionOpened((WebSocketConnection)c));
                WebSocketServerSSL?.Start(c => OnWebConnectionOpened((WebSocketConnection)c));

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
                Task.Factory.StartNew(ProcessOutgoingMessages);
            }
        }

        public async Task Stop()
        {
            if (Interlocked.Exchange(ref _stopping, 1) == 0 && _running != 0)
            {
                NotifyAll(new CommunicationData_Disconnect(DisconnectReaseon.ServerShutdown));

                Stopwatch sw = new();

                sw.Start();

                while (_outgoing.Count > 0 && sw.ElapsedMilliseconds < 5_000)
                    await Task.Delay(50);

                _running = 0;

                TCPListener.Stop();

                foreach (PlayerInfo info in _players.Values)
                    info.Dispose();

                _players.Clear();
                _stopping = 0;

                "Server stopped. Press [ENTER] to terminate.".Info(LogSource.Server);
            }
        }

        public void Dispose()
        {
            Stop().GetAwaiter().GetResult();
            WebSocketServer.Dispose();
            WebSocketServerSSL?.Dispose();
        }

        public void AddBannedNames(IEnumerable<string> names)
        {
            foreach (string name in names)
                BannedNames.Add(name);
        }

        private void AddPlayer(Player player, Union<BinaryWriter, WebSocketConnection> connection)
        {
            _players[player] = new(this, player, connection);

            $"{player} connected.".Info(LogSource.Server);

            NotifyAllExcept(player, new CommunicationData_PlayerJoined(player.UUID));
            Notify(player, new CommunicationData_ServerInformation(ServerName, _players.Values.ToArray(p => p.Player.UUID)));
            OnPlayerJoined?.Invoke(player);

            ChangeAdminStatus(player, AdminUUIDs?.Contains(player.UUID) ?? false);

            if (CurrentGame is Game game)
                BroadcastCurrentGameState(game, new[] { player });
        }

        private void RemovePlayer(Player player)
        {
            RemovePlayerFromCurrentGame(player);
            _players.TryRemove(player, out PlayerInfo? info);
            info?.Dispose();

            $"{player} disconnected.".Warn(LogSource.Server);

            NotifyAllExcept(player, new CommunicationData_PlayerLeft(player.UUID));
            OnPlayerLeft?.Invoke(player);
        }

        public async Task KickPlayer(Player player)
        {
            Notify(player, new CommunicationData_Disconnect(DisconnectReaseon.Kicked));

            while (_outgoing.ToArray().Any(t => t.Item1 == player))
                await Task.Delay(30);

            RemovePlayer(player);
        }

        public void ChangeAdminStatus(Player player, bool make_admin)
        {
            if (this[player] is PlayerInfo info)
                info.IsAdmin = make_admin;

            AdminUUIDs ??= new();

            if (make_admin)
                AdminUUIDs.Add(player.UUID);
            else
                AdminUUIDs.Remove(player.UUID);
        }

        public Player? TryResolvePlayer(string name_or_uuid)
        {
            name_or_uuid = name_or_uuid.Trim();

            (Player player, PlayerInfo info)[] players = (_players as IReadOnlyDictionary<Player, PlayerInfo>).FromDictionary();
            Player? player = null;
            Player[] match;

            if (Guid.TryParse(name_or_uuid, out Guid uuid))
                player = players.FirstOrDefault(p => p.player.UUID == uuid).player;

            player ??= players.FirstOrDefault(p => p.info.Name.Equals(name_or_uuid, StringComparison.InvariantCultureIgnoreCase)).player;

            if (player is null)
            {
                match = players.ToArrayWhere(p => p.info.Name.Contains(name_or_uuid, StringComparison.InvariantCultureIgnoreCase), p => p.player);

                if (match.Length == 0)
                    match = players.Select(p => p.player).ToArrayWhere(p => p.UUID.ToString().Contains(name_or_uuid, StringComparison.InvariantCultureIgnoreCase));

                if (match.Length == 1)
                    player = match[0];
            }

            return player;
        }

        #region COMMUNICATION LOGIC

        private async Task OnTCPClientConnected(TcpClient client)
        {
            Player? player = null;

            try
            {
                $"Incoming connection from {client.Client?.RemoteEndPoint}.".Log(LogSource.Server);

                using (NetworkStream stream = client.GetStream())
                using (BinaryReader reader = new(stream))
                using (BinaryWriter writer = new(stream))
                {
                    Guid uuid = reader.ReadNative<Guid>();

                    AddPlayer(player = new(uuid) { Client = client }, writer);
                    writer.Write(ServerName);

                    ArraySegment<byte> keepalive_buffer = new byte[1];

                    while (_running != 0 && client.Client is { } socket)
                        if (socket.Poll(0, SelectMode.SelectRead) && await socket.ReceiveAsync(keepalive_buffer, SocketFlags.Peek) == 0)
                        {
                            $"Connection to {player} lost.".Err(LogSource.Server);

                            break;
                        }
                        else
                        {
                            bool idle = true;

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
                AddPlayer(player, socket);

                socket.OnClose = () => RemovePlayer(player);
                socket.OnBinary = bytes => OnWebsocketMessage(socket, player, bytes);
                socket.OnMessage = message => OnWebsocketMessage(socket, player, message);

                while (_running != 0 && socket.IsAvailable)
                    await Task.Delay(20);
            }

            socket.Close();
        }

        private async Task ProcessOutgoingMessages()
        {
            while (_running != 0)
            {
                bool any = false;

                while (_outgoing.TryDequeue(out var item))
                    try
                    {
                        any = true;
                        (Player player, RawCommunicationPacket packet) = item;

                        if (this[player] is PlayerInfo info)
                        {
                            info.Connection.Match(
                                packet.WriteTo,
                                ws => ws.Send(packet.Message.Match(RawCommunicationPacket.Encoding.GetString, LINQ.id))
                            );

                            $"(conv:{packet.ConversationIdentifier}) {packet} sent to '{player}'.".Log(LogSource.Server);
                        }
                        else
                            $"(conv:{packet.ConversationIdentifier}) Target '{player}' not found.".Warn(LogSource.Server);
                    }
                    catch (Exception ex)
                    {
                        ex.Warn(LogSource.Server);
                    }

                if (!any)
                    await Task.Delay(1);
            }
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

                        CommunicationData? message = item.Item2.DeserializeData();

                        $"(conv:{item.Item2.ConversationIdentifier}) {message} received from '{item.Item1}'.".Log(LogSource.Server);

                        CommunicationData? reply = await ProcessIncomingMessages(item.Item1, message, item.Item2.ConversationIdentifier != Guid.Empty);

                        if (reply is { })
                        {
                            $"(conv:{item.Item2.ConversationIdentifier}) Sending {reply} to '{item.Item1}'...".Log(LogSource.Server);

                            _outgoing.Enqueue((item.Item1, RawCommunicationPacket.SerializeData(item.Item2.ConversationIdentifier, reply)));
                        }
                    }
                    catch (Exception ex)
                    {
                        ex.Warn(LogSource.Server);
                    }

                if (!any)
                    await Task.Delay(1);
            }
        }

        private void OnWebsocketMessage(WebSocketConnection socket, Player player, Union<byte[], string> message)
        {
            if (RawCommunicationPacket.DeserializeData(message)?.guid is Guid guid)
                _incoming.Enqueue((player, new(guid, message)));
            else
                $"Unable fetch conversation GUID from '{player}':\nJSON = {message.Match(RawCommunicationPacket.Encoding.GetString, LINQ.id)}".Err(LogSource.WebServer);
        }

        public void Notify(Player player, params CommunicationData[] data)
        {
            foreach (CommunicationData d in data)
            {
                $"Sending {d} to '{player}'...".Log(LogSource.Server);

                _outgoing.Enqueue((player, RawCommunicationPacket.SerializeData(Guid.Empty, d)));
            }
        }

        public void Notify(IEnumerable<Player> players, params CommunicationData[] data) => players.Do(p => Notify(p, data));

        public void NotifyAll(params CommunicationData[] data) => Notify(_players.Keys, data);

        public void NotifyAllExcept(Player player, params CommunicationData[] data) => NotifyAllExcept(new[] { player }, data);

        public void NotifyAllExcept(IEnumerable<Player> players, params CommunicationData[] data) => Notify(_players.Keys.Except(players), data);

        public void NotifyGamePlayers(params CommunicationData[] data)
        {
            if (CurrentGame is Game game)
                Notify(game.Players.Select(p => p.Player), data);
        }

        #endregion

        private async Task<CommunicationData?> ProcessIncomingMessages(Player player, CommunicationData? message, bool reply_requested)
        {
            switch (message)
            {
                case CommunicationData_PlayerNameChangeRequest(string name):
                    {
                        name = name.Trim();
                        string? banned = null;

                        if (name.Length < 2 && name.Length > 32)
                            return new CommunicationData_SuccessError(false, "Your name is shorter than 2 characters or is longer than 32 characters.");
                        else if (!name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_'))
                            return new CommunicationData_SuccessError(false, "Your name may only contain alpha-numeric characters and spaces or hyphens.");
                        else if (_players.Values.Any(info => name.Equals(info.Name, StringComparison.InvariantCultureIgnoreCase)))
                            return new CommunicationData_SuccessError(false, $"The name '{name}' has already been taken by somebody else.");

                        foreach (string s in BannedNames.ToArray())
                            if (name.Contains(s, StringComparison.InvariantCultureIgnoreCase))
                            {
                                banned = s;

                                break;
                            }

                        if (banned is null)
                        {
                            if (this[player] is PlayerInfo info)
                                info.Name = name;

                            return CommunicationData_SuccessError.OK;
                        }
                        else
                            return new CommunicationData_SuccessError(false, "Your name contains a banned word :(");
                    }
                case CommunicationData_GameJoinRequest:
                    if (CurrentGame?.TryAddPlayer(player, out _) ?? false)
                        return CommunicationData_SuccessError.OK;
                    else
                        return new CommunicationData_SuccessError(false, "Unable to join game: The game is either currently running or has not yet been initiated by an administrator.");
                case CommunicationData_GameLeaveRequest:
                    return new CommunicationData_SuccessError(CurrentGame?.RemovePlayer(player) ?? false, null);
                case CommunicationData_PlayerQueryInfo request:
                    {
                        if (this[request.UUID] is PlayerInfo info)
                            return new CommunicationData_PlayerInfo(true, info.Name, info.IsAdmin, CurrentGame?.Players?.Any(p => p.Player.UUID == request.UUID) ?? false);
                        else
                            return CommunicationData_PlayerInfo.NotFound;
                    }
                case CommunicationData_GameDraw(DrawSource source_pile):
                    if (CurrentGame?.CurrentPlayer___DrawCard(source_pile is DrawSource.DiscardPile) ?? false)
                        return CommunicationData_SuccessError.OK;
                    else
                        return new CommunicationData_SuccessError(false, "Invalid game move.");
                case CommunicationData_GameSwap(int row, int column):
                    if (CurrentGame?.CurrentPlayer___SwapDrawn(row, column) ?? false)
                        return CommunicationData_SuccessError.OK;
                    else
                        return new CommunicationData_SuccessError(false, "Invalid game move.");
                case CommunicationData_GameDiscard:
                    if (CurrentGame?.CurrentPlayer___DiscardDrawn() ?? false)
                        return CommunicationData_SuccessError.OK;
                    else
                        return new CommunicationData_SuccessError(false, "Invalid game move.");
                case CommunicationData_GameUncover(int row, int column):
                    if (CurrentGame?.CurrentPlayer___UncoverCard(row, column) ?? false)
                        return CommunicationData_SuccessError.OK;
                    else
                        return new CommunicationData_SuccessError(false, "Invalid game move.");
                case CommunicationData_AdminCommand admin_request:
                    if (this[player] is not { IsAdmin: true })
                        return new CommunicationData_SuccessError(false, "Unable to execute command: You must be an administrator.");
                    else
                        switch (admin_request)
                        {
                            case CommunicationData_AdminKickPlayer(Guid uuid):
                                {
                                    if (this[uuid]?.Player is Player p)
                                    {
                                        await KickPlayer(p);

                                        return CommunicationData_SuccessError.OK;
                                    }
                                    else
                                        return new CommunicationData_SuccessError(false, $"Unable to find a player with the UUID {uuid:B}.");
                                }
                            case CommunicationData_AdminRemovePlayerFromGame(Guid uuid):
                                {
                                    if (this[uuid]?.Player is Player p)
                                    {
                                        RemovePlayerFromCurrentGame(p);

                                        return CommunicationData_SuccessError.OK;
                                    }
                                    else
                                        return new CommunicationData_SuccessError(false, $"Unable to find a player with the UUID {uuid:B}.");
                                }
                            case CommunicationData_AdminMakeAdmin(Guid uuid):
                                {
                                    if (this[uuid]?.Player is Player p)
                                    {
                                        ChangeAdminStatus(p, true);

                                        return CommunicationData_SuccessError.OK;
                                    }
                                    else
                                        return new CommunicationData_SuccessError(false, $"Unable to find a player with the UUID {uuid:B}.");
                                }
                            case CommunicationData_AdminMakeRegular(Guid uuid):
                                {
                                    if (this[uuid]?.Player is Player p)
                                    {
                                        ChangeAdminStatus(p, false);

                                        return CommunicationData_SuccessError.OK;
                                    }
                                    else
                                        return new CommunicationData_SuccessError(false, $"Unable to find a player with the UUID {uuid:B}.");
                                }
                            case CommunicationData_AdminGameReset:
                                CurrentGame?.FinishGame();
                                ResetNewGame();

                                return CommunicationData_SuccessError.OK;
                            case CommunicationData_AdminGameStart:
                                {
                                    if (CurrentGame is Game game)
                                    {
                                        game.DealCardsAndRestart();

                                        NotifyGamePlayers(new CommunicationData_Notification("The game has started! Good luck to everyone!"));
                                    }

                                    return CommunicationData_SuccessError.OK;
                                }
                            case CommunicationData_AdminGameStop:
                                CurrentGame?.FinishGame();

                                return CommunicationData_SuccessError.OK;
                            case CommunicationData_AdminServerStop:
                                await Task.Factory.StartNew(Stop);

                                return CommunicationData_SuccessError.OK;
                            default:
                                return OnIncomingData?.Invoke(player, message, reply_requested);
                        }
                default:
                    return OnIncomingData?.Invoke(player, message, reply_requested);
            }
        }

        #region ACTUAL GAME LOGIC

        public void ResetNewGame()
        {
            if (CurrentGame is Game current)
            {
                current.OnPlayerAdded -= CurrentGame_OnPlayerAdded;
                current.OnPlayerRemoved -= CurrentGame_OnPlayerRemoved;
                current.OnGameStateChanged -= CurrentGame_OnGameStateChanged;
            }

            CurrentGame = new();
            CurrentGame.OnPlayerAdded += CurrentGame_OnPlayerAdded;
            CurrentGame.OnPlayerRemoved += CurrentGame_OnPlayerRemoved;
            CurrentGame.OnGameStateChanged += CurrentGame_OnGameStateChanged;

            BroadcastCurrentGameState(CurrentGame);
        }

        public int TryAddAllConnectedPlayersToGame()
        {
            int count = 0;

            if (CurrentGame is { CurrentGameState: GameState.Stopped })
                foreach ((Player player, _) in _players)
                    if (TryAddPlayerToGame(player))
                        ++count;

            return count;
        }

        public bool TryAddPlayerToGame(Player player) => CurrentGame?.TryAddPlayer(player, out _) ?? false;

        public void RemovePlayerFromCurrentGame(Player player) => CurrentGame?.RemovePlayer(player);

        private void CurrentGame_OnGameStateChanged(Game game)
        {
            if (game is { CurrentGameState: GameState.Running or GameState.FinalRound, WaitingFor: GameWaitingFor.NextPlayer })
                if (game.CurrentPlayer___FinishesFinalRound())
                {
                    if (game.FinishGame().FirstOrDefault().Player is Player winner)
                        NotifyAll(new CommunicationData_PlayerWin(winner.UUID));
                }
                else
                {
                    if (game.CurrentPlayer___TryEnterFinalRound())
                        NotifyGamePlayers(new CommunicationData_Notification("Final game round!"));

                    game.NextPlayer();
                }
            else
                BroadcastCurrentGameState(game);
        }

        private void CurrentGame_OnPlayerRemoved(Game game, Player player) => NotifyAll(new CommunicationData_PlayerLeftGame(player.UUID));

        private void CurrentGame_OnPlayerAdded(Game game, Player player) => NotifyAll(new CommunicationData_PlayerJoinedGame(player.UUID));

        private void BroadcastCurrentGameState(Game game) => BroadcastCurrentGameState(game, _players.Keys);

        private void BroadcastCurrentGameState(Game game, IEnumerable<Player> targets)
        {
            (Player Player, int Points)[] leader_board = game.GetCurrentLeaderBoard();
            CommunicationData_GameUpdate.GameUpdatePlayerData[] players = game.Players.ToArray(p =>
            {
                int cols = p.Dimensions.columns;
                int rows = p.Dimensions.rows;
                Card?[] cards = new Card?[cols * rows];

                for (int i = 0; i < cards.Length; ++i)
                    cards[i] = p.GameField[i / cols, i % cols] switch { (Card c, true) => c, _ => null };

                int index = (from t in leader_board.WithIndex()
                             where t.Item.Player == p.Player
                             select t.Index + 1).FirstOrDefault();

                index = index == 0 ? leader_board.Length - 1 : index - 1;

                return new CommunicationData_GameUpdate.GameUpdatePlayerData(p.Player.UUID, cols, rows, cards, p.CurrentlyDrawnCard, index);
            });

            foreach (Player player in targets)
            {
                Notify(player, new CommunicationData_GameUpdate(
                    game.DrawPile.Count,
                    game.DiscardPile.Count,
                    game.DiscardedCard,
                    game.CurrentGameState,
                    game.WaitingFor,
                    players,
                    game.CurrentGameState is GameState.Running or GameState.FinalRound ? game.CurrentPlayerIndex : -1,
                    game.Players.FirstOrDefault(p => p.Player == player)?.CurrentlyDrawnCard,
                    Game.MAX_PLAYERS
                ));
            }

            NotifyAll(new CommunicationData_LeaderBoard(game.GetCurrentLeaderBoard().ToArray(t => new CommunicationData_LeaderBoard.LeaderBoardEntry(t.Player.UUID, t.Points))));
        }

        #endregion

        public static async Task<GameServer> CreateGameServer(ServerConfig config) =>
            new(config.local_server ? new(config.address, config.port_tcp, config.port_ws, config.port_wss)
                                    : await ConnectionString.GetMyConnectionString(config.port_tcp, config.port_ws, config.port_wss), config);
    }

    public sealed record ServerConfig(
        string address,
        ushort port_tcp,
        ushort port_ws,
        ushort port_wss,
        bool local_server,
        string? certificate_path,
        string pfx_password,
        string server_name,
        string[] banned_names,
        Guid[]? admin_uuids
    );

    [Obsolete]
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


        public event Action<CommunicationData?>? OnIncomingData;


        public GameClient(Guid uuid, ConnectionString connection_string)
        {
            ConnectionString = connection_string;
            _open_conversations = new();
            _outgoing = new();
            _incoming = new();

            IPEndPoint ep = new(IPAddress.Parse(ConnectionString.Address), ConnectionString.Ports.CSharp);
            TcpClient client = new();
            client.Connect(ep);

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

            $"Connected to '{ServerName}' via {ep}.".Info(LogSource.Client);
        }

        private async Task CommunicationHandler()
        {
            ArraySegment<byte> keepalive_buffer = new byte[1];
            TcpClient? tcp_client = null;

            if ((Player.Client?.Is(out tcp_client) ?? false) && tcp_client?.Client is Socket client)
                while (IsAlive)
                    if (tcp_client?.Client is null || (client.Poll(0, SelectMode.SelectRead) && await client.ReceiveAsync(keepalive_buffer, SocketFlags.Peek) == 0))
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

        public void SendMessage(CommunicationData data) => _outgoing.Enqueue(RawCommunicationPacket.SerializeData(Guid.Empty, data));

        public async Task<CommunicationData?> SendMessageAndWaitForReply(CommunicationData data)
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

            TcpClient? client = null;

            if (Player.Client?.Is(out client) ?? false)
            {
                client.Close();
                client.Dispose();
            }
        }
    }
}
