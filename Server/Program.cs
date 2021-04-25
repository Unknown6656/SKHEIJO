using System;
using System.Threading.Tasks;
using SKHEIJO;
using Unknown6656.Common;
using Unknown6656.IO;

namespace Server
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            Logger.Start();


            //Game game = new(new Player[] { new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()) });
            //game.ResetAndDealCards(150, 0);



            // using GameServer server = await GameServer.CreateGameServer(14488, 14499, "lol kay");
            using GameServer server = GameServer.CreateLocalGameServer("127.0.0.1", 14488, 14499, "lol kay");

            Console.WriteLine(server.ConnectionString);
            Console.WriteLine("\npress ESC to exit.");

            server.OnIncomingData += (p, m, r) => From.String(From.Bytes(m).ToHexString());
            server.Start();

            while (server.IsRunning)
            {
                do
                    while (!Console.KeyAvailable)
                        await Task.Delay(20);
                while (Console.ReadKey(true).Key != ConsoleKey.Escape);

                server.Stop();
            }

            await Logger.Stop();
        }
    }
}
