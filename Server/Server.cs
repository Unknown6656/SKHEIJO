using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using System.Net.Http;
using System.Text;
using System.Linq;
using System.Net;
using System.IO;
using System;

using Fleck;

using Unknown6656.Mathematics.Numerics;
using Unknown6656.Common;
using Unknown6656.IO;
using Unknown6656;

namespace SKHEIJO
{
    public record Player(Guid UUID)
    {
        public static Player SERVER { get; } = new(Guid.Empty);

        public WebSocketConnection? Client { get; set; }

        public bool IsServer => Equals(SERVER);


        public override string ToString() => $"{UUID} {(Client?.ConnectionInfo is { } conn ? $"{conn.ClientIpAddress}:{conn.ClientPort}" : "not connected")}";
    }

    public sealed class ConnectionString
    {
        private const string _ROTATE = "E=k1bq7Rgc+jXypOKsft2iQwHv0dSxWTJLDGa56uAIB/zYNrZ3oPm9n4FVMCehlU8";

        public (ushort WS, ushort WSS) Ports { get; }
        public string Address { get; }
        public bool IsIPv6 { get; }


        public ConnectionString(IPAddress address, ushort port_ws, ushort port_wss)
            : this(address.ToString(), port_ws, port_wss, address.AddressFamily is AddressFamily.InterNetworkV6)
        {
        }

        public ConnectionString(string address, ushort port_ws, ushort port_wss, bool is_v6 = false)
        {
            Address = address;
            Ports = (port_ws, port_wss);
            IsIPv6 = is_v6;
        }

        public ConnectionString With(IPAddress address) => new(address, Ports.WS, Ports.WSS);

        public ConnectionString With(string address) => new(address, Ports.WS, Ports.WSS);

        public override int GetHashCode() => ToString().GetHashCode();

        public override bool Equals(object? obj) => obj is ConnectionString cs && cs.ToString() == ToString();

        public override string ToString()
        {
            string addr = Address;

            if (IPAddress.TryParse(addr, out IPAddress? ip) && ip?.AddressFamily is AddressFamily.InterNetworkV6)
                addr = $"[{addr}]";

            char[] chars = From.String($"{addr}${Ports.WS}${Ports.WSS}${(IsIPv6 ? 1 : 0)}").ToBase64().ToCharArray();

            for (int i = 0; i < chars.Length; ++i)
                chars[i] = _ROTATE[(_ROTATE.Length * 20 - chars.Length + _ROTATE.IndexOf(chars[i]) + 1 + i) % _ROTATE.Length] switch
                {
                    '/' => '-',
                    '=' => '_',
                    char c => c
                };

            return new(chars);
        }

        public static ConnectionString FromString(string connection_string)
        {
            string[] parts = From.Base64(connection_string).ToString().Split('$');

            return new(
                parts[0],
                ushort.Parse(parts[1]),
                ushort.Parse(parts[2]),
                parts[3] == "1"
            );
        }

        public static async Task<ConnectionString> GetMyConnectionString(ushort port_ws, ushort port_wss)
        {
            using HttpClient client = new();
            string html = await client.GetStringAsync("https://api64.ipify.org/");
            IPAddress ip = IPAddress.Parse(html);

            return new(ip, port_ws, port_wss);
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
        public WebSocketConnection? Connection { get; }

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
            get => IsServer || _is_admin;
            set
            {
                if (!IsServer)
                    if (_is_admin != value)
                    {
                        _is_admin = value;

                        Server.NotifyAll(new CommunicationData_PlayerInfoChanged(Player.UUID));
                    }
            }
        }

        public bool IsServer => Player.IsServer;


        internal PlayerInfo(GameServer server, Player player, WebSocketConnection? connection)
        {
            Server = server;
            Player = player;
            Connection = connection;
            _name = $"Player-{player.UUID.GetHashCode():x8}";
        }

        public override string ToString() => $"{Name}{(IsAdmin ? " (admin)" : "")}: {Player}";

        public void Dispose() => Player.Client?.Close();
    }

    public sealed class GameServer
        : IDisposable
    {
        private const int UPDATE_TOKEN_INTERVAL_MS = 5_000;
        private const int SAVE_SERVER_INTERVAL_MS = 7_000;
        private static readonly Regex REGEX_UUID = new(@"^\{\{[0-9A-F]{8}[-]?(?:[0-9A-F]{4}[-]?){3}[0-9A-F]{12}\}\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly XorShift _random = new();
        private static readonly object _mutex = new();

        internal readonly ConcurrentDictionary<Player, PlayerInfo> _players;
        private readonly ConcurrentQueue<(Player, RawCommunicationPacket)> _incoming;
        private readonly ConcurrentQueue<(Player, RawCommunicationPacket)> _outgoing;
        private readonly ConcurrentQueue<CommunicationData_ChatMessages.ChatMessage> _chat;
        private readonly ConcurrentDictionary<Guid, ServerConfig.UserToken> _user_tokens;
        private readonly List<ServerConfig.HighScore> _highscores;
        private readonly HashSet<Guid> _banned_guids;
        private readonly HashSet<Guid> _admin_guids;
        private ServerConfig _initial_config;
        private volatile int _running;
        private volatile int _stopping;

        public string ServerName { get; }
        public HashSet<string> BannedNames { get; }
        public Game? CurrentGame { get; private set; }
        public ConnectionString ConnectionString { get; }
        public WebSocketServer WebSocketServer { get; }
        public WebSocketServer? WebSocketServerSSL { get; }
        public FileInfo ChatMessagesPath { get; }
        public FileInfo HighscoresPath { get; }
        public FileInfo UserTokensPath { get; }
        public FileInfo AdministratorsPath { get; }
        public FileInfo BannedUsersPath { get; }
        public FileInfo ConfigurationPath { get; }

        public bool IsRunning => _running != 0;

        public PlayerInfo? this[Player player] => _players.TryGetValue(player, out PlayerInfo? info) ? info : null;

        public PlayerInfo? this[Guid guid] => _players.ToArray().SelectWhere(entry => entry.Key.UUID == guid, entry => entry.Value).FirstOrDefault();


        public event IncomingDataDelegate? OnIncomingData;
        public event Action<Player>? OnPlayerJoined;
        public event Action<Player>? OnPlayerLeft;


        private GameServer(FileInfo config_path, ConnectionString connection_string, ServerConfig config)
        {
            Directory.SetCurrentDirectory(config_path.Directory!.FullName);

            _initial_config = config;
            ConfigurationPath = config_path;

            string config_dir = config_path.Directory!.FullName;

            ChatMessagesPath = new(Path.Combine(config_dir, config.paths.chat));
            HighscoresPath = new(Path.Combine(config_dir, config.paths.highscores));
            UserTokensPath = new(Path.Combine(config_dir, config.paths.user_tokens));
            AdministratorsPath = new(Path.Combine(config_dir, config.paths.administrators));
            BannedUsersPath = new(Path.Combine(config_dir, config.paths.banned));

            ServerName = config.server_name;
            ConnectionString = connection_string;
            BannedNames = new(StringComparer.InvariantCultureIgnoreCase);
            CurrentGame = null;
            _players = new();
            _incoming = new();
            _outgoing = new();
            _highscores = new();

            if (HighscoresPath.Exists)
                _highscores.AddRange(From.File(HighscoresPath).ToJSON<List<ServerConfig.HighScore>>().OrderBy(hs => hs.points));

            _chat = ChatMessagesPath.Exists ? From.File(ChatMessagesPath).ToJSON<ConcurrentQueue<CommunicationData_ChatMessages.ChatMessage>>() : new();
            _user_tokens = UserTokensPath.Exists ? From.File(UserTokensPath).ToJSON<ConcurrentDictionary<Guid, ServerConfig.UserToken>>() : new();
            _admin_guids = AdministratorsPath.Exists ? From.File(AdministratorsPath).ToJSON<HashSet<Guid>>() : new();
            _banned_guids = BannedUsersPath.Exists ? From.File(BannedUsersPath).ToJSON<HashSet<Guid>>() : new();

            WebSocketServer = new($"ws://{(connection_string.IsIPv6 ? "[::]" : "0.0.0.0")}:{connection_string.Ports.WS}", true);
            WebSocketServer.ListenerSocket.NoDelay = true;
            WebSocketServer.RestartAfterListenError = true;
            WebSocketServer.EnabledSslProtocols = SslProtocols.Ssl3 | SslProtocols.Ssl2 | SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls;

            if (config.certificate is string)
            {
                WebSocketServerSSL = new($"wss://{(connection_string.IsIPv6 ? "[::]" : "0.0.0.0")}:{connection_string.Ports.WSS}", true);
                WebSocketServerSSL.ListenerSocket.NoDelay = WebSocketServer.ListenerSocket.NoDelay;
                WebSocketServerSSL.RestartAfterListenError = WebSocketServer.RestartAfterListenError;
                WebSocketServerSSL.EnabledSslProtocols = WebSocketServer.EnabledSslProtocols;
                WebSocketServerSSL.Certificate = new X509Certificate2(config.certificate, config.pfx_password);
            }

            AddBannedNames(config.banned_names);

            if (config.init_board_size is ( > 1, > 1))
                PlayerState.InitialDimensions = config.init_board_size;
        }

        public void Start()
        {
            _stopping = 0;

            if (Interlocked.Exchange(ref _running, 1) == 0)
            {
                _players[Player.SERVER] = new(this, Player.SERVER, null)
                {
                    IsAdmin = true,
                    Name = "[SERVER]",
                };

                ResetNewGame();

                WebSocketServer.Start(c => OnWebConnectionOpened((WebSocketConnection)c));
                WebSocketServerSSL?.Start(c => OnWebConnectionOpened((WebSocketConnection)c));

                Task.Factory.StartNew(async delegate
                {
                    while (_running != 0)
                    {
                        SaveServer();

                        await Task.Delay(SAVE_SERVER_INTERVAL_MS);
                    }

                    SaveServer();
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
                SaveServer();

                Stopwatch sw = new();

                sw.Start();

                while (_outgoing.Count > 0 && sw.ElapsedMilliseconds < 5_000)
                    await Task.Delay(50);

                _running = 0;

                foreach (PlayerInfo info in _players.Values)
                    info.Dispose();

                _players.Clear();
                _stopping = 0;

                await Task.Factory.StartNew(async () =>
                {
                    await Task.Delay(500);

                    "\n\n---------------------------\nServer stopped.\nPress [ENTER] to terminate.".Warn(LogSource.Server);
                });
            }
        }

        public void SaveServer()
        {
            lock (_mutex)
            {
                $"Saving server config to '{ConfigurationPath}'...".Log(LogSource.Server);

                From.JSON(_initial_config = _initial_config with
                {
                    // address = ConnectionString.Address,
                    banned_names = BannedNames.ToArray(),
                    init_board_size = PlayerState.InitialDimensions,
                }).ToFile(ConfigurationPath);

                From.JSON(_chat.ToArray()).ToFile(ChatMessagesPath);
                From.JSON(_user_tokens).ToFile(UserTokensPath);
                From.JSON(_admin_guids).ToFile(AdministratorsPath);
                From.JSON(_banned_guids).ToFile(BannedUsersPath);
                From.JSON(_highscores).ToFile(HighscoresPath);
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

            SaveServer();
        }

        private void AddPlayer(Player player, WebSocketConnection connection)
        {
            _players[player] = new(this, player, connection);

            $"{player} connected.".Info(LogSource.Server);

            NotifyAllExcept(player, new CommunicationData_PlayerJoined(player.UUID));
            Notify(player, new CommunicationData_ServerInformation(ServerName, _players.Values.ToArray(p => p.Player.UUID)));
            OnPlayerJoined?.Invoke(player);

            ChangeAdminStatus(player, _admin_guids?.Contains(player.UUID) ?? false);

            if (CurrentGame is Game game)
                BroadcastCurrentGameState(game, new[] { player });

            Notify(player, new CommunicationData_ServerHighScores(_highscores.ToArray()));
            Notify(player, new CommunicationData_ChatMessages(_chat.ToArray()));
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

            if (make_admin)
                _admin_guids.Add(player.UUID);
            else
                _admin_guids.Remove(player.UUID);

            SaveServer();
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

        private async void OnWebConnectionOpened(WebSocketConnection socket)
        {
            (Guid UUID, string Token)? auth = null;

            socket.OnBinary = bytes =>
            {
                try
                {
                    unsafe
                    {
                        string[] str = BytewiseEncoding.Instance.GetString(bytes).Split('|');

                        if (str.Length > 1 && Guid.TryParse(str[0], out Guid uuid))
                        {
                            auth = (uuid, str[1]);
                            socket.OnBinary = null;
                        }
                    }
                }
                catch
                {
                    socket.Close();
                }
            };

            while (_running != 0 && socket.IsAvailable && auth is null)
                await Task.Delay(10);

            if (auth is (Guid guid, string current_token))
            {
                CommunicationData_SuccessError login_result = CommunicationData_SuccessError.OK;
                Player player = new(guid)
                {
                    Client = socket
                };

                if (guid == Guid.Empty || _players.Keys.Any(p => p.UUID == guid))
                    try
                    {
                        $"Rejecting login from {player} due to {(guid == Guid.Empty ? "empty" : "duplicate")} UUID.".Err(LogSource.Server);

                        login_result = new(false, guid == Guid.Empty ? "Login as [SERVER] is not permitted from a web interface." : "It seems that you are already logged in in an other tab.");
                    }
                    catch (Exception ex)
                    {
                        ex.Warn(LogSource.Server);
                    }
                else if (!_user_tokens.TryGetValue(guid, out ServerConfig.UserToken? expected))
                    _user_tokens[guid] = new(new(), DateTime.Now);
                else if (expected.tokens.Count > 1 && !expected.tokens.Contains(current_token))
                {
                    $"Rejecting login from {player} due to invalid token (expected {expected}, got '{current_token}').".Err(LogSource.Server);

                    login_result = new(false, "You do not posess the correct authentication to connect to the server. Please reset your security token or contact the server administrator.");
                }

                await socket.Send(RawCommunicationPacket.SerializeData(Guid.Empty, login_result).Message.Match(RawCommunicationPacket.Encoding.GetString, LINQ.id));

                if (login_result.Success)
                {
                    Stopwatch token_stopwatch = new();

                    AddPlayer(player, socket);

                    socket.OnClose = () => RemovePlayer(player);
                    socket.OnBinary = bytes => OnWebsocketMessage(socket, player, bytes);
                    socket.OnMessage = message => OnWebsocketMessage(socket, player, message);
                    token_stopwatch.Start();

                    Queue<string> tokens = _user_tokens[guid].tokens;

                    while (_running != 0 && socket.IsAvailable)
                        if (token_stopwatch.ElapsedMilliseconds > UPDATE_TOKEN_INTERVAL_MS)
                        {
                            current_token = From.Bytes(_random.NextBytes(32)).ToHexString();
                            _user_tokens[guid] = _user_tokens[guid] with { date = DateTime.Now, tokens = tokens };

                            tokens.Enqueue(current_token);

                            Notify(player, new CommunicationData_UserToken(current_token));

                            while (tokens.Count > 10)
                                tokens.Dequeue();

                            token_stopwatch.Restart();

                            $"Updated security token for {player}: {current_token}".Log(LogSource.Server);
                        }
                        else
                            await Task.Delay(20);
                }
                else
                    await Task.Delay(250);
            }

            socket.Close();
        }

        private void OnWebsocketMessage(WebSocketConnection _, Player player, Union<byte[], string> message)
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
                            await (info.Connection?.Send(packet.Message.Match(RawCommunicationPacket.Encoding.GetString, LINQ.id)) ?? Task.CompletedTask);

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

        internal async Task<CommunicationData?> ProcessIncomingMessages(Player player, CommunicationData? message, bool reply_requested)
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
                            return new CommunicationData_SuccessError(false, "Your name may only contain alpha-numeric characters, spaces, hyphens, and underscores.");
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
                            return new CommunicationData_PlayerInfo(true, info.Name, info.IsAdmin, info.IsServer, CurrentGame?.Players?.Any(p => p.Player.UUID == request.UUID) ?? false);
                        else
                            return CommunicationData_PlayerInfo.NotFound;
                    }
                case CommunicationData_GameDraw(Pile source_pile):
                    if (CurrentGame?.CurrentPlayer___DrawCard(source_pile is Pile.Discard) ?? false)
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
                case CommunicationData_SendChatMessage(string content):
                    if (ProcessChatMessage(player.UUID, content))
                        return CommunicationData_SuccessError.OK;
                    else
                        return new CommunicationData_SuccessError(false, "The message cannot be empty.");
                case CommunicationData_AdminCommand admin_request:
                    if (this[player] is not { IsAdmin: true })
                        return new CommunicationData_SuccessError(false, "Unable to execute command: You must be an administrator.");
                    else
                        switch (admin_request)
                        {
                            case CommunicationData_AdminKickPlayer(Guid uuid):
                                {
                                    if (this[uuid]?.Player is { IsServer: false } p)
                                    {
                                        await KickPlayer(p);

                                        return CommunicationData_SuccessError.OK;
                                    }
                                    else
                                        return new CommunicationData_SuccessError(false, $"Unable to find a player with the UUID {uuid:B}.");
                                }
                            case CommunicationData_AdminRemovePlayerFromGame(Guid uuid):
                                {
                                    if (this[uuid]?.Player is { IsServer: false } p)
                                    {
                                        RemovePlayerFromCurrentGame(p);

                                        return CommunicationData_SuccessError.OK;
                                    }
                                    else
                                        return new CommunicationData_SuccessError(false, $"Unable to find a player with the UUID {uuid:B}.");
                                }
                            case CommunicationData_AdminMakeAdmin(Guid uuid):
                                {
                                    if (this[uuid]?.Player is { IsServer: false } p)
                                    {
                                        ChangeAdminStatus(p, true);

                                        return CommunicationData_SuccessError.OK;
                                    }
                                    else
                                        return new CommunicationData_SuccessError(false, $"Unable to find a player with the UUID {uuid:B}.");
                                }
                            case CommunicationData_AdminMakeRegular(Guid uuid):
                                {
                                    if (this[uuid]?.Player is { IsServer: false } p)
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
                            case CommunicationData_AdminInitialBoardSize(int Columns, int Rows):
                                if (Columns >= 2 && Rows >= 2)
                                {
                                    PlayerState.InitialDimensions = (Rows, Columns);

                                    NotifyAll(admin_request);

                                    if (CurrentGame is { Players: { } players, CurrentGameState: GameState.Stopped })
                                    {
                                        Player[] ps = players.ToArray(p => p.Player);

                                        ResetNewGame();

                                        foreach (Player p in ps)
                                            TryAddPlayerToGame(p);
                                    }

                                    SaveServer();

                                    return CommunicationData_SuccessError.OK;
                                }
                                else
                                    return new CommunicationData_SuccessError(false, "The board must have an initial size of at least 2x2.");
                            case CommunicationData_AdminRequestWinAnimation(Guid UUID):
                                NotifyAll(new CommunicationData_PlayerWin(UUID));

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

        public bool ProcessChatMessage(Guid UUID, string content)
        {
            content = content.Trim().SplitIntoLines().Select(s => s.Trim()).StringJoin("\n");

            StringBuilder sanitized = new();
            HashSet<Guid> mentioned = new();
            char? surrogate = null;
            bool whitespace = true;
            int newlines = 10;

            for (int index = 0; index < content.Length; ++index)
            {
                char c = content[index];

                if (surrogate is char surr && !char.IsSurrogatePair(surr, c))
                    surrogate = null;

                if (char.IsLetterOrDigit(c))
                {
                    sanitized.Append(c);
                    whitespace = false;
                    newlines = 0;
                }
                else if (c is '\n')
                {
                    ++newlines;
                    whitespace = true;

                    if (newlines <= 2)
                        sanitized.Append("<br/>");
                }
                else if (c is '\r' || char.IsWhiteSpace(c) || char.IsControl(c) || char.IsSeparator(c))
                {
                    if (!whitespace)
                        sanitized.Append(' ');

                    whitespace = true;
                }
                else if (char.IsHighSurrogate(c))
                    surrogate = c;
                else if (surrogate is char)
                {
                    int code_point = char.ConvertToUtf32(surrogate.Value, c);

                    sanitized.Append($"&#{code_point};");
                    surrogate = null;
                }
                else if (c is '{' && index < content.Length - 1 && content[index..].Match(REGEX_UUID, out Match match))
                {
                    if (Guid.TryParse(match.Value[1..^1], out Guid uuid))
                        mentioned.Add(uuid);

                    sanitized.Append(match.Value);
                    whitespace = false;
                    newlines = 0;
                    index += match.Length - 1;
                }
                else
                {
                    sanitized.Append($"&#{(int)c};");
                    whitespace = false;
                    newlines = 0;
                }
            }

            content = sanitized.ToString().Trim();

            if (string.IsNullOrEmpty(content))
                return false;

            CommunicationData_ChatMessages.ChatMessage message = new(UUID, DateTime.Now, content);

            message.Log(LogSource.Chat);

            _chat.Enqueue(message);
            NotifyAll(new CommunicationData_ChatMessages(message));

            foreach (Guid uuid in mentioned)
                if (uuid != UUID && this[uuid]?.Player is Player p)
                    Notify(p, new CommunicationData_ChatMessageMention(UUID));

            return true;
        }

        #endregion
        #region ACTUAL GAME LOGIC

        public void ResetNewGame()
        {
            if (CurrentGame is Game current)
            {
                current.OnPlayerAdded -= CurrentGame_OnPlayerAdded;
                current.OnPlayerRemoved -= CurrentGame_OnPlayerRemoved;
                current.OnGameStateChanged -= CurrentGame_OnGameStateChanged;
                CurrentGame.OnColumnDeleted -= CurrentGame_OnColumnDeleted;
                CurrentGame.OnCardFlipped -= CurrentGame_OnCardFlipped;
                CurrentGame.OnCardMoved -= CurrentGame_OnCardMoved;
            }

            CurrentGame = new();
            CurrentGame.OnPlayerAdded += CurrentGame_OnPlayerAdded;
            CurrentGame.OnPlayerRemoved += CurrentGame_OnPlayerRemoved;
            CurrentGame.OnGameStateChanged += CurrentGame_OnGameStateChanged;
            CurrentGame.OnColumnDeleted += CurrentGame_OnColumnDeleted;
            CurrentGame.OnCardFlipped += CurrentGame_OnCardFlipped;
            CurrentGame.OnCardMoved += CurrentGame_OnCardMoved;

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
            game.CurrentPlayer___RemoveFullColumnsOfIdenticalCards(out _);

            if (game is { CurrentGameState: GameState.Running or GameState.FinalRound, WaitingFor: GameWaitingFor.NextPlayer })
                if (game.CurrentPlayer___FinishesFinalRound())
                {
                    (Player Player, int Points)[] leaderboard = game.FinishGame();
                    int highscore_count = _highscores.Count;

                    if (leaderboard.FirstOrDefault().Player is Player winner)
                        NotifyAll(new CommunicationData_PlayerWin(winner.UUID));

                    foreach ((Player player, int points) in leaderboard.Reverse())
                        if (_highscores.Count == 0 || (_highscores[0].points > points))
                            _highscores.Insert(0, new(player.UUID, this[player]?.Name, DateTime.Now, points, game.InitialRows, game.InitialColumns, game.Players.Count));

                    if (_highscores.Count != highscore_count)
                    {
                        NotifyAll(new CommunicationData_ServerHighScores(_highscores.ToArray()));
                        SaveServer();
                    }
                }
                // ELSE-IF is pretty important here because tryenterfinalround sets currentgamestate, which invokes this method, which in turn then calls 'next player'
                else if (game.CurrentPlayer___TryEnterFinalRound() && game.FinalRoundInitiator?.UUID is Guid final)
                    NotifyGamePlayers(new CommunicationData_FinalRound(final));
                else
                    game.NextPlayer();
            else
                BroadcastCurrentGameState(game);
        }

        private void CurrentGame_OnPlayerRemoved(Game game, Player player) => NotifyAll(new CommunicationData_PlayerLeftGame(player.UUID));

        private void CurrentGame_OnPlayerAdded(Game game, Player player) => NotifyAll(new CommunicationData_PlayerJoinedGame(player.UUID));

        private void CurrentGame_OnCardMoved(Game game, CommunicationData_AnimateMoveCard animation) => NotifyAllExcept(this[animation.UUID]!.Player, animation);

        private void CurrentGame_OnCardFlipped(Game game, CommunicationData_AnimateFlipCard animation) => NotifyAll(animation);

        private void CurrentGame_OnColumnDeleted(Game game, CommunicationData_AnimateColumnDeletion animation) => NotifyAll(animation);

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

                int points = cards.Sum(c => c?.Value ?? 0);
                int index = (from t in leader_board.WithIndex()
                             where t.Item.Player == p.Player
                             select t.Index + 1).FirstOrDefault();

                index = index == 0 ? leader_board.Length - 1 : index - 1;

                if (index >= leader_board.Length - 1)
                    points *= 2;

                return new CommunicationData_GameUpdate.GameUpdatePlayerData(p.Player.UUID, cols, rows, cards, p.CurrentlyDrawnCard, index, points);
            });

            foreach (Player player in targets)
                Notify(player, new CommunicationData_GameUpdate(
                    game.DrawPile.Count,
                    game.DiscardPile.Count,
                    game.DiscardedCard,
                    game.CurrentGameState,
                    game.WaitingFor,
                    players,
                    game.CurrentGameState is GameState.Running or GameState.FinalRound ? game.CurrentPlayerIndex : -1,
                    game.Players.FirstOrDefault(p => p.Player == player)?.CurrentlyDrawnCard,
                    Game.MAX_PLAYERS,
                    game.FinalRoundInitiator?.UUID ?? Guid.Empty
                ));

            NotifyAll(new CommunicationData_LeaderBoard(game.GetCurrentLeaderBoard().ToArray(t => new CommunicationData_LeaderBoard.LeaderBoardEntry(t.Player.UUID, t.Points))));
            NotifyAll(new CommunicationData_AdminInitialBoardSize(PlayerState.InitialDimensions.columns, PlayerState.InitialDimensions.rows));
        }

        #endregion

        public static async Task<GameServer> CreateGameServer(FileInfo config_path)
        {
            ServerConfig? config = null;

            try
            {
                config = From.File(config_path).ToJSON<ServerConfig>();
            }
            catch
            {
            }

            config ??= new(
                address: "0.0.0.0",
                port_ws: 42087,
                port_wss: 42088,
                local_server: false,
                paths: new(
                    chat: "messages.json",
                    highscores: "highscores.json", 
                    user_tokens: "user_tokens.json",
                    administrators: "administrators.json",
                    banned: "banned.json"
                ),
                certificate: null,
                pfx_password: "",
                server_name: "test server",
                banned_names: new string[] { "admin", "server" },
                init_board_size: PlayerState.InitialDimensions
            );

            return new(config_path, config.local_server ? new(config.address, config.port_ws, config.port_wss) : await ConnectionString.GetMyConnectionString(config.port_ws, config.port_wss), config);
        }
    }

    public sealed record ServerConfig(
        string address,
        ushort port_ws,
        ushort port_wss,
        bool local_server,
        ServerConfig.Paths paths,
        string? certificate,
        string pfx_password,
        string server_name,
        string[] banned_names,
        ServerConfig.BoardSize init_board_size
    )
    {
        public sealed record Paths(string chat, string highscores, string user_tokens, string administrators, string banned);

        public sealed record HighScore(Guid uuid, string? name, DateTime date, int points, int rows, int columns, int players);

        public sealed record UserToken(Queue<string> tokens, DateTime date)
        {
            public override string ToString() => $"{{[{tokens.StringJoin(", ")}], {date}}}";
        }

        public sealed record BoardSize(int rows, int columns)
        {
            public static implicit operator (int rows, int cols)(BoardSize size) => (size.rows, size.columns);

            public static implicit operator BoardSize((int rows, int cols) size) => new(size.rows, size.cols);
        }
    }
}
