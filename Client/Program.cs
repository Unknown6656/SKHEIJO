using System.Diagnostics;
using System.Windows;
using System;

using Unknown6656.Common;

namespace SKHEIJO
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] argv)
        {
            Logger.Start();

            $"Arguments({argv.Length}): \"{argv.StringJoin("\", \"")}\"".Log(LogSource.UI);

            int ret = -1;
            Configuration configuration = Configuration.TryReadConfig(new("user.dat")) ?? Configuration.TryReadConfig(new("default.dat")) ?? Configuration.Default;

            if (configuration.Client is null)
                configuration = configuration with
                {
                    Client = new($"{Environment.UserName}-{Guid.NewGuid().GetHashCode() & 0xffff:x4}", Guid.NewGuid(), "")
                };

            try
            {
                ConnectWindowInterop interop = new(configuration);

                do
                {
                    $"Starting {nameof(ConnectWindow)} ...".Log(LogSource.UI);

                    ConnectWindow window = new(interop);

                    window.ShowDialog();
                    configuration = interop.Configuration;

                    if (!interop.DialogResult)
                        break;
                    else if (interop.GameClient is null)
                        ; // TODO : message box with error
                }
                while (interop.GameClient is null);



                /// TODO : ????




                if (interop.GameClient is { } g)
                {
                    $"Starting {nameof(GameWindow)} ...".Log(LogSource.UI);

                    GameWindow window = new(g);

                    window.ShowDialog();
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

            Logger.Stop().GetAwaiter().GetResult();

            return ret;
        }
    }
}
