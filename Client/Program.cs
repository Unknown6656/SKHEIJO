using System.Diagnostics;
using System;

using Unknown6656.Common;
using System.Windows;
using Unknown6656.IO;
using Unknown6656.Imaging;
using Unknown6656.Imaging.Effects;

namespace SKHEIJO
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] argv)
        {
            //From.File(@"L:\Projects.VisualStudio\SKHEIJO\artwork\raw\heikki1.jpeg").ToBitmap().ToARGB32().ApplyEffect(new HexagonalPixelation(40)).Save(
            //    @"L:\Projects.VisualStudio\SKHEIJO\artwork\hex.png");

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
                        MessageBox.Show(@"
The connection code seems to be invalid or the game server could not be contacted. Please verify that you entered the code correctly and that your internet connection is stable.

Consider contacting the person who gave you the connection code should this error persist over multiple retries. The reason could be that the game server is experiencing some down times. Furhtermore, the game server needs to be accessible to the wider internet. A connection failure may indicate that the server is inacessible due to firewall restrictions.
", "Connection failed", MessageBoxButton.OK, MessageBoxImage.Warning);
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
