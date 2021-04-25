using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.Windows;
using System;

using Unknown6656.Common;

namespace SKHEIJO
{
    public sealed class App
        : Application
    {
        public string[] Arguments { get; }


        static App() => Logger.Start();

        public App(string[] argv) => Arguments = argv;

        public static async Task<int> Main(string[] argv)
        {
            Logger.Start();

            $"Arguments({argv.Length}): \"{argv.StringJoin("\", \"")}\"".Log(LogSource.UI);

            Configuration configuration = Configuration.TryReadConfig(new("user.dat")) ?? Configuration.TryReadConfig(new("default.dat")) ?? Configuration.Default;
            int ret = -1;

            if (configuration.Client is null)
                configuration = configuration with
                {
                    Client = new($"{Environment.UserName}-{Guid.NewGuid().GetHashCode() & 0xffff:x4}", Guid.NewGuid(), "")
                };

            try
            {
                GameClient? game = null;
                bool retry = false;

                do
                {
                    $"Starting {nameof(ConnectWindow)} STA thread...".Log(LogSource.UI);

                    Thread thread = new(() =>
                    {
                        App app = new(argv);
                        Box<Configuration> box_c = configuration;
                        Box<GameClient> box_g = new();
                        ConnectWindow window = new(box_c, box_g);

                        ret = app.Run(window);
                        retry = window.DialogResult ?? false;
                        configuration = box_c!;
                        game = box_g;
                    });

                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();

                    $"{nameof(ConnectWindow)} STA thread started.".Log(LogSource.UI);

                    thread.Join();

                    if (!retry)
                        break;
                    else if (game is null)
                        ; // TODO : message box with error
                }
                while (game is null);



                /// TODO : ????




                if (game is { } g)
                {
                    $"Starting {nameof(GameWindow)} STA thread...".Log(LogSource.UI);

                    Thread thread = new(() =>
                    {
                        App app = new(argv);
                        GameWindow window = new(game);

                        ret = app.Run(window);
                    });

                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();

                    $"{nameof(GameWindow)} STA thread started.".Log(LogSource.UI);

                    thread.Join();
                }
            }
            catch (Exception ex)
            when (!Debugger.IsAttached)
            {
                ex.Err(LogSource.Client);
            }
            finally
            {
                configuration.WriteConfig(new("user.dat"));
            }

            await Logger.Stop();

            return ret;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            $"Application started.".Log(LogSource.UI);

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            $"Application exiting with {e.ApplicationExitCode}".Log(LogSource.UI);

            base.OnExit(e);
        }
    }
}



//string? s;
//
//do
//    s = Console.ReadLine();
//while (s is null);
//
//using GameClient client = new(Guid.NewGuid(), s);
//
//Console.WriteLine("type 'q' to exit");
//
//while (true)
//{
//    string line = Console.ReadLine() ?? "";
//
//    if (line == "q")
//        break;
//    else
//        From.Bytes(await client.SendMessageAndWaitForReply(From.String(line))).ToString().Log();
//}
//
//client.Dispose();


