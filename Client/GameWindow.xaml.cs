using System.Windows;


namespace SKHEIJO
{
    public sealed partial class GameWindow
        : Window
    {
        public GameClient GameClient { get; }


        public GameWindow(GameClient game)
        {
            InitializeComponent();

            GameClient = game;
        }



        //        From.Bytes(await client.SendMessageAndWaitForReply(From.String(line))).ToString().Log();
    }
}
