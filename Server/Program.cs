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
            using GameServer server = GameServer.CreateLocalGameServer("192.168.0.26", 14488, 14499, "lol kay");
            
            Console.WriteLine($@"
--------------------------------------------
    INIVATION LINK:

    {server.ConnectionString}
--------------------------------------------
");
            Console.WriteLine("\ntype 'q' to exit.");

            server.OnIncomingData += (p, o, r) =>
            {
                o.Log(LogSource.Server);

                return o;
            };
            server.Start();

            while (server.IsRunning)
            {
                string cmd;

                do
                {
                    cmd = Console.ReadLine() ?? "";
                    server.NotifyAll(new CommunicationData_ServerInformation(cmd));
                }
                while (cmd != "q");

                server.Stop();
            }

            await Logger.Stop();
        }
    }
}
