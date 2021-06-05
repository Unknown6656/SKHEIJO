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

namespace Server
{
    public static class Program
    {
        private static readonly DirectoryInfo ASM_DIR = new FileInfo(Assembly.GetExecutingAssembly().Location).Directory!;


        public static async Task Main(string[] _)
        {
            Console.Clear();
            Logger.MinimumSeverityLevelForAll = LogSeverity.Debug;
            Logger.MinimumSeverityLevel[LogSource.Server] = LogSeverity.Info;
            Logger.MinimumSeverityLevel[LogSource.WebServer] = LogSeverity.Info;
            Logger.Start();

            FileInfo settings_path = new($"{ASM_DIR.FullName}/server-config.json");
            using GameServer server = await GameServer.CreateGameServer(settings_path);
            void print_codes()
            {
                IPHostEntry dns_entry = Dns.GetHostEntry(Dns.GetHostName());
                (string a, ConnectionString c)[] conn_str = dns_entry.AddressList.Select(a => (a.ToString(), server.ConnectionString.With(a))).Concat(
                                                            dns_entry.Aliases.Select(a => (a, server.ConnectionString.With(a)))).ToArray();

                AppDomain.CurrentDomain.ProcessExit += (_, _) => server.SaveServer();

                Console.WriteLine($@"
------------------------------------------------------------------------------------------------------------------
    IP:                 {server.ConnectionString.Address}
    PORTS:              {server.ConnectionString.Ports}
    INIVATION CODE:     {server.ConnectionString}
------------------------------------------------------------------------------------------------------------------
    ALT. INVITATIONS:
    {conn_str.Select(t => $"{t.a,38}: {t.c}").StringJoin("\n    ")}
------------------------------------------------------------------------------------------------------------------
type 'stop' to exit or '?' for help.
");
            };

            print_codes();

            server.OnIncomingData += (p, o, r) =>
            {
                o.Log(LogSource.Server);

                return o;
            };
            server.Start();

            Regex R_OP = new(@"^op\s+(?<p>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex R_UNOP = new(@"^unop\s+(?<p>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex R_KICK = new(@"^kick\s+(?<p>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex R_SAY = new(@"^notify\s+(?<m>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex R_SAY_PRIVATE = new(@"^notify@\s*(?<p>.+)\s*@\s*(?<m>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex R_ADD = new(@"^add\s+(?<p>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex R_REMOVE = new(@"^remove\s+(?<p>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex R_WIN = new(@"^win\s+(?<p>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex R_CHAT = new(@"^chat\s+(?<m>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex R_RESIZE = new(@"^resize\s+(?<rows>\d+)\s+(?<cols>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            do
            {
                while (!Console.KeyAvailable && server.IsRunning)
                    await Task.Delay(10);

                if (!server.IsRunning)
                    break;

                string cmd = (Console.ReadLine() ?? "").Trim();

                if (cmd.ToLowerInvariant() == "stop")
                    break;
                else if (cmd == "?")
                    @"

--------------------------------------- HELP ---------------------------------------
?                               display this help text
clear                           clear console
codes                           print all invitation codes
op <player>                     makes <player> admin
unop <player>                   makes <player> regular player
kick <player>                   kicks <player> from the server
notify <message>                sends <message> to every player
notify@ <player> @ <message>    sends <message> to <player>
chat <message>                  write <message> to the chat
reset                           reset/restart game
add <player>                    adds <player> to the game
aadd                            adds all (possible) players to the game
remove <player>                 removes <player> from the game
l                               lists game players
ll                              lists server players
deal                            reset game and deal cards
finish                          forces to finish the game right now
resize <rows> <columns>         resizes the player area to the given size
game                            displays game information
win <player>                    emulates win notification for <player>
dbg                             enable/disable debug mode
stop                            stop server
------------------------------------------------------------------------------------

".Info(LogSource.Server);
                else if (cmd.ToLowerInvariant() == "clear")
                    Console.Clear();
                else if (cmd.ToLowerInvariant() == "reset")
                    server.ResetNewGame();
                else if (cmd.ToLowerInvariant() == "codes")
                    print_codes();
                else if (cmd.ToLowerInvariant() == "aadd")
                    server.TryAddAllConnectedPlayersToGame();
                else if (cmd.ToLowerInvariant() == "ll")
                    ($"{server._players} Players:\n- " + server._players.Select(kvp => kvp.Value).StringJoin("\n- ")).Info(LogSource.Server);
                else if (cmd.ToLowerInvariant() == "l")
                    server.CurrentGame?.Players.Select(p => "\n" + p).StringJoin("\n").Info(LogSource.Server);
                else if (cmd.ToLowerInvariant() == "deal")
                    server.CurrentGame?.DealCardsAndRestart();
                else if (cmd.ToLowerInvariant() == "game")
                    server.CurrentGame?.ToString().Info(LogSource.Server);
                else if (cmd.ToLowerInvariant() == "finish")
                    server.CurrentGame?.FinishGame();
                else if (cmd.ToLowerInvariant() == "dbg")
                    Logger.MinimumSeverityLevel[LogSource.Server] =
                    Logger.MinimumSeverityLevel[LogSource.WebServer] =
                        Logger.MinimumSeverityLevel[LogSource.Server] == LogSeverity.Info ||
                        Logger.MinimumSeverityLevel[LogSource.WebServer] == LogSeverity.Info ? LogSeverity.Debug : LogSeverity.Info;
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
                        [R_RESIZE] = m =>
                        {
                            if (int.TryParse(m.Groups["rows"].Value, out int rows) && rows > 1 &&
                                int.TryParse(m.Groups["cols"].Value, out int columns) && columns > 1)
                                server.ProcessIncomingMessages(Player.SERVER, new CommunicationData_AdminInitialBoardSize(columns, rows), false).GetAwaiter().GetResult();
                        },
                        [R_CHAT] = m => server.ProcessChatMessage(Guid.Empty, m.Groups["m"].Value),
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
            await Logger.Stop();
        }
    }
}
