using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Unknown6656.Common;
using Unknown6656.IO;

namespace SKHEIJO
{
    public record Player(int UID)
    {
        public TcpClient? Client { get; init; }
    }

    public sealed class ConnectionString
    {
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

        public override string ToString() =>
            From.ArrayOfSources(
                From.Unmanaged(IsIPv6),
                From.String(Address.ToString()),
                From.Unmanaged(Port)
            ).ToBase64();

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
            From[] parts = From.Base64(connection_string)
                               .ToArrayOfSources();

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

    public sealed class GameServer
        : IDisposable
    {
        public ConnectionString ConnectionString { get; }
        public TcpListener Listener { get; }
        public bool IsRunning => _running != 0;

        private readonly ConcurrentHashSet<Player> _players;
        private volatile int _running;
        private volatile int _playerid;


        private GameServer(ConnectionString connection_string)
        {
            ConnectionString = connection_string;
            Listener = new(new IPEndPoint(connection_string.IsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, connection_string.Port));
            _players = new();
            _playerid = 1;
        }

        public static GameServer CreateLocalGameServer(string addr, ushort port) => new(new(addr, port));

        public static async Task<GameServer> CreateGameServer(ushort port) => new(await ConnectionString.GetMyConnectionString(port));

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
                    Console.WriteLine(ex);
                }
        }

        private async Task OnClientConnected(TcpClient client)
        {
            Player player = new(Interlocked.Increment(ref _playerid))
            {
                Client = client
            };

            try
            {
                _players.Add(player);

                Console.WriteLine(client.Client.RemoteEndPoint + " connected.");

                using (NetworkStream stream = client.GetStream())
                using (BinaryReader reader = new(stream))
                using (BinaryWriter writer = new(stream))
                {
                    writer.Write(player.UID);

                    ArraySegment<byte> keepalive_buffer = new byte[1];

                    while (_running != 0)
                        if (client.Client.Poll(0, SelectMode.SelectRead) && await client.Client.ReceiveAsync(keepalive_buffer, SocketFlags.Peek) == 0)
                            break; // client disconnected
                        else
                        {



                            // TODO
                        }
                }

                client.Close();
            }
            finally
            {
                Console.WriteLine(client.Client.RemoteEndPoint + " disconnected.");

                _players.Remove(player);
                client.Dispose();
            }
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _running, 0) != 0)
            {
                Listener.Stop();

                foreach (Player player in _players)
                {
                    player.Client?.Close();
                    player.Client?.Dispose();
                }

                _players.Clear();
            }
        }

        public void Dispose() => Stop();


        private async Task OnIncomingDataAsync(Player from, From message)
        {

        }
    }

    public sealed class GameClient
        : IDisposable
    {
        public ConnectionString ConnectionString { get; }
        public NetworkStream Stream { get; }
        public BinaryWriter Writer { get; }
        public BinaryReader Reader { get; }
        public Player Player { get; }


        public GameClient(ConnectionString connection_string)
        {
            ConnectionString = connection_string;

            TcpClient client = new();
            client.Connect(ConnectionString.EndPoint);

            Stream = client.GetStream();
            Writer = new(Stream);
            Reader = new(Stream);
            Player = new(Reader.ReadInt32())
            {
                Client = client
            };
        }

        public void Dispose()
        {
            Reader.Close();
            Writer.Close();
            Stream.Close();
            Player.Client?.Close();
            Player.Client?.Dispose();
        }
    }
}
