using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq;
using System.Net;
using System.IO;
using System;

using SKHEIJO;

using Unknown6656.Common;
using Unknown6656.IO;

namespace Server
{
    public static class Program
    {
        private static readonly DirectoryInfo ASM_DIR = new FileInfo(Assembly.GetExecutingAssembly().Location).Directory!;



        public static async Task Main(string[] args)
        {
            Console.Clear();

            Logger.MinimumSeverityLevel = LogSeverity.Info;
            Logger.Start();

            string settings_path = $"{ASM_DIR.FullName}/server-config.json";
            ServerConfig? config = From.File(settings_path).ToJSON<ServerConfig>();

            config ??= new("0.0.0.0", 42087, 42088, 42089, false, null, "", "test server", new string[] { "admin", "server" }, Array.Empty<Guid>());

            using GameServer server = await GameServer.CreateGameServer(config);
            IPHostEntry dns_entry = Dns.GetHostEntry(Dns.GetHostName());
            (string a, ConnectionString c)[] conn_str = dns_entry.AddressList.Select(a => (a.ToString(), server.ConnectionString.With(a))).Concat(
                                                        dns_entry.Aliases.Select(a => (a, server.ConnectionString.With(a)))).ToArray();

            Console.WriteLine($@"
----------------------------------------------------------------------------------------
    IP:                 {server.ConnectionString.Address}
    PORTS:              {server.ConnectionString.Ports}
    INIVATION LINK:     {server.ConnectionString}
----------------------------------------------------------------------------------------
    ALT. INVITATIONS:
    {conn_str.Select(t => $"{t.a,40}: {t.c}").StringJoin("\n    ")}
----------------------------------------------------------------------------------------
type 'q' to exit or '?' for help.
");

            server.OnIncomingData += (p, o, r) =>
            {
                o.Log(LogSource.Server);

                return o;
            };
            server.Start();

            Regex R_OP = new(@"^o\s+(?<p>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex R_UNOP = new(@"^u\s+(?<p>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex R_KICK = new(@"^k\s+(?<p>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex R_SAY = new(@"^@\s+(?<m>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex R_SAY_PRIVATE = new(@"^@(?<p>.+)@\s+(?<m>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex R_ADD = new(@"^a\s+(?<p>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex R_REMOVE = new(@"^v\s+(?<p>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex R_WIN = new(@"^w\s+(?<p>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            do
            {
                while (!Console.KeyAvailable)
                    await Task.Delay(10);

                string cmd = (Console.ReadLine() ?? "").Trim();

                if (cmd.ToLowerInvariant() == "q")
                    break;
                else if (cmd == "?")
                    @"

--------------------------------------- HELP ---------------------------------------
?                       display this help text
c                       clear console
o <player>              makes <player> admin
u <player>              makes <player> regular player
k <player>              kicks <player> from the server
@ <message>             sends <message> to every player
@<player>@ <message>    sends <message> to <player>
r                       reset/restart game
a <player>              adds <player> to the game
aa                      adds all (possible) players to the game
v <player>              removes <player> from the game
l                       lists game players
ll                      lists server players
d                       reset game and deal cards
g                       displays game information
w <player>              emulates win notification for <player>

vv                      toggles verbose output on/off
q                       stop server
------------------------------------------------------------------------------------

".Info(LogSource.Server);
                else if (cmd.ToLowerInvariant() == "c")
                    Console.Clear();
                else if (cmd.ToLowerInvariant() == "r")
                    server.ResetNewGame();
                else if (cmd.ToLowerInvariant() == "aa")
                    server.TryAddAllConnectedPlayersToGame();
                else if (cmd.ToLowerInvariant() == "vv")
                    Logger.MinimumSeverityLevel = Logger.MinimumSeverityLevel == LogSeverity.Debug ? LogSeverity.Info : LogSeverity.Debug;
                else if (cmd.ToLowerInvariant() == "ll")
                    ($"{server._players} Players:\n- " + server._players.Select(kvp => kvp.Value).StringJoin("\n- ")).Info(LogSource.Server);
                else if (cmd.ToLowerInvariant() == "l")
                    server.CurrentGame?.Players.Select(p => "\n" + p).StringJoin("\n").Info(LogSource.Server);
                else if (cmd.ToLowerInvariant() == "d")
                    server.CurrentGame?.DealCardsAndRestart();
                else if (cmd.ToLowerInvariant() == "g")
                    server.CurrentGame?.ToString().Info(LogSource.Server);
                else
                    cmd.Match(new Dictionary<Regex, Action<Match>>()
                    {
                        [R_OP] = m =>
                        {
                            if (server.TryResolvePlayer(m.Groups["p"].Value) is Player player)
                                server.ChangeAdminStatus(player, true);
                            else
                                $"Unknown player '{m.Groups["p"]}'.".Err(LogSource.Server);
                        },
                        [R_UNOP] = m =>
                        {
                            if (server.TryResolvePlayer(m.Groups["p"].Value) is Player player)
                                server.ChangeAdminStatus(player, false);
                            else
                                $"Unknown player '{m.Groups["p"]}'.".Err(LogSource.Server);
                        },
                        [R_KICK] = m =>
                        {
                            if (server.TryResolvePlayer(m.Groups["p"].Value) is Player player)
                                server.KickPlayer(player).GetAwaiter().GetResult();
                            else
                                $"Unknown player '{m.Groups["p"]}'.".Err(LogSource.Server);
                        },
                        [R_SAY_PRIVATE] = m =>
                        {
                            if (server.TryResolvePlayer(m.Groups["p"].Value) is Player player)
                                server.Notify(player, new CommunicationData_Notification(m.Groups["m"].Value));
                            else
                                $"Unknown player '{m.Groups["p"]}'.".Err(LogSource.Server);
                        },
                        [R_WIN] = m =>
                        {
                            if (server.TryResolvePlayer(m.Groups["p"].Value) is Player player)
                                server.NotifyAll(new CommunicationData_PlayerWin(player.UUID));
                            else
                                $"Unknown player '{m.Groups["p"]}'.".Err(LogSource.Server);
                        },
                        [R_SAY] = m => server.NotifyAll(new CommunicationData_Notification(m.Groups["m"].Value)),
                        [R_ADD] = m =>
                        {
                            if (server.TryResolvePlayer(m.Groups["p"].Value) is Player player)
                                server.TryAddPlayerToGame(player);
                            else
                                $"Unknown player '{m.Groups["p"]}'.".Err(LogSource.Server);
                        },
                        [R_REMOVE] = m =>
                        {
                            if (server.TryResolvePlayer(m.Groups["p"].Value) is Player player)
                                server.RemovePlayerFromCurrentGame(player);
                            else
                                $"Unknown player '{m.Groups["p"]}'.".Err(LogSource.Server);
                        },
                        // TODO
                    });
            }
            while (server.IsRunning);

            await server.Stop();

            config = config with { admin_uuids = server.AdminUUIDs?.ToArray() ?? config.admin_uuids };

            From.JSON(config).ToFile(settings_path);

            await Logger.Stop();
        }
    }
}
