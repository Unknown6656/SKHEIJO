using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
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
            Logger.Start();


            //Game game = new(new Player[] { new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()) });
            //game.ResetAndDealCards(150, 0);

            ServerConfig? config = JsonSerializer.Deserialize<ServerConfig>(From.File($"{ASM_DIR.FullName}/server-config.json").ToString());

            config ??= new("0.0.0.0", 42088, 42089, "test server", new string[] { "admin", "server" });

            // using GameServer server = await GameServer.CreateGameServer(config);
            using GameServer server = GameServer.CreateLocalGameServer(config);
            
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

            string cmd;

            do
            {
                cmd = Console.ReadLine() ?? "";
                //TODO:?
            }
            while (cmd != "q");

            await server.Stop();
            await Logger.Stop();
        }
    }
}
