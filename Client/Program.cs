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

            $"Arguments({argv.Length}): \"{argv.StringJoin("\", \"")}\"".Log();

            Configuration configuration = Configuration.TryReadConfig(new("user.dat")) ?? Configuration.TryReadConfig(new("default.dat")) ?? Configuration.Default;
            int ret = -1;

            try
            {
                $"Starting STA thread...".Log();

                Thread thread = new(() =>
                {
                    App app = new(argv);
                    ConnectWindow window = new(configuration);

                    ret = app.Run(window);
                    configuration = window.Configuration;
                });

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();

                $"STA thread started.".Log();

                thread.Join();
            }
            catch (Exception ex)
            when (!Debugger.IsAttached)
            {
                ex.Err();
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
            $"Application started.".Log();

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            $"Application exiting with {e.ApplicationExitCode}".Log();

            base.OnExit(e);
        }
    }
}
