using System.Windows.Documents;
using System.Windows;
using System.Threading.Tasks;
using System.Net.Mail;
using System;

namespace SKHEIJO
{
    public sealed class ConnectWindowInterop
    {
        public Configuration Configuration { get; set; }
        public GameClient? GameClient { get; set; }
        public bool DialogResult { get; set; }


        public ConnectWindowInterop(Configuration configuration) => Configuration = configuration;
    }

    public sealed partial class ConnectWindow
        : Window
    {
        public ConnectWindowInterop Interop { get; }


        public ConnectWindow(ConnectWindowInterop interop)
        {
            InitializeComponent();

            Interop = interop;
            Loaded += ConnectWindow_Loaded;
            btn_cancel.Click += Btn_cancel_Click;
            btn_connect.Click += Btn_connect_Click;
        }

        private void ConnectWindow_Loaded(object sender, RoutedEventArgs e)
        {
            lb_author.Text = Interop.Configuration.Author.Name;
            tb_connect_string.Text = Interop.Configuration.Client?.LastConnection;
            tbl_contact.Inlines.Clear();

            if (Interop.Configuration.Author.Email is string mail)
            {
                if (!mail.Contains("://"))
                {
                    bool is_mail = MailAddress.TryCreate(mail, out _);

                    mail = $"{(is_mail ? "mailto" : "https")}://{mail}";
                }

                tbl_contact.Inlines.Add(new Hyperlink(new Run(Interop.Configuration.Author.Email))
                {
                    NavigateUri = new(mail)
                });

                if (Interop.Configuration.Author.Phone is string)
                    tbl_contact.Inlines.Add(new Run(" or "));
            }

            if (Interop.Configuration.Author.Phone is string phone)
                tbl_contact.Inlines.Add(new Hyperlink(new Run(phone))
                {
                    NavigateUri = new("tel:" + phone)
                });
        }

        private void Btn_cancel_Click(object sender, RoutedEventArgs e)
        {
            Interop.DialogResult = false;

            Close();
        }

        private async void Btn_connect_Click(object sender, RoutedEventArgs e)
        {
            if (Interop.Configuration.Client is Client client)
                try
                {
                    string text = tb_connect_string.Text.Trim();

                    Interop.GameClient = await Task.Factory.StartNew(() => new GameClient(client.UUID, text));
                }
                catch (Exception ex)
                {
                    ex.Err(LogSource.UI);
                }

            Interop.DialogResult = true;

            Close();
        }
    }
}
