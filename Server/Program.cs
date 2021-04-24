using System;
using System.Threading.Tasks;
using System.Xml.Schema;
using SKHEIJO;

namespace Server
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            Game game = new(new Player[] { new(1), new(2), new(3) });
            game.ResetAndDealCards(150, 0);

            Console.WriteLine(game.CurrentPlayer.ToString());



            // using GameServer server = await GameServer.CreateGameServer(14488);
            using GameServer server = GameServer.CreateLocalGameServer("127.0.0.1", 14488);

            Console.WriteLine(server.ConnectionString);
            Console.WriteLine("\npress ESC to exit.");

            server.Start();

            while (server.IsRunning)
                if (Console.KeyAvailable && Console.ReadKey(true).Key is ConsoleKey.Escape)
                    server.Stop();
        }
    }
}
