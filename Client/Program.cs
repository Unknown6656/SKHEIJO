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
                $"Starting STA thread...".Log(LogSource.UI);

                Thread thread = new(() =>
                {
                    App app = new(argv);
                    Box<Configuration> box_c = configuration;
                    ConnectWindow window = new(box_c);

                    ret = app.Run(window);
                    configuration = box_c!;
                });

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();

                $"STA thread started.".Log(LogSource.UI);

                thread.Join();
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
