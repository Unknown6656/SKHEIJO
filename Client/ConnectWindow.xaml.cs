using System.Windows.Documents;
using System.Windows;
using System.Net.Mail;
using System;

namespace SKHEIJO
{
    public sealed partial class ConnectWindow
        : Window
    {
        public Box<Configuration> Configuration { get; }
        public Box<GameClient> GameClient { get; }


        public ConnectWindow(Box<Configuration> configuration, Box<GameClient> game)
        {
            InitializeComponent();

            GameClient = game;
            Configuration = configuration;
            Loaded += ConnectWindow_Loaded;
            btn_cancel.Click += Btn_cancel_Click;
            btn_connect.Click += Btn_connect_Click;
        }

        private void ConnectWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Configuration conf = Configuration.Value!;

            lb_author.Text = conf.Author.Name;
            tb_connect_string.Text = conf.Client?.LastConnection;
            tbl_contact.Inlines.Clear();

            if (conf.Author.Email is string mail)
            {
                if (!mail.Contains("://"))
                {
                    bool is_mail = MailAddress.TryCreate(mail, out _);

                    mail = $"{(is_mail ? "mailto" : "https")}://{mail}";
                }

                tbl_contact.Inlines.Add(new Hyperlink(new Run(mail))
                {
                    NavigateUri = new(mail)
                });

                if (conf.Author.Phone is string)
                    tbl_contact.Inlines.Add(new Run(" or "));
            }

            if (conf.Author.Phone is string phone)
                tbl_contact.Inlines.Add(new Hyperlink(new Run(phone))
                {
                    NavigateUri = new("tel:" + phone)
                });
        }

        private void Btn_cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Btn_connect_Click(object sender, RoutedEventArgs e)
        {
            if (Configuration.Value?.Client is Client client)
                try
                {
                    string text = tb_connect_string.Text.Trim();

                    GameClient.Value = new(client.UUID, text);
                }
                catch (Exception ex)
                {
                    ex.Err(LogSource.UI);
                }

            DialogResult = true;

            Close();
        }
    }
}
