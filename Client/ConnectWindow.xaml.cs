using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SKHEIJO
{
    public sealed partial class ConnectWindow
        : Window
    {
        public Box<Configuration> Configuration { get; }


        public ConnectWindow(Box<Configuration> configuration)
        {
            InitializeComponent();

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

        private void Btn_cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Btn_connect_Click(object sender, RoutedEventArgs e)
        {
            if (Configuration.Value?.Client is Client client)
                try
                {
                    string text = tb_connect_string.Text.Trim();
                    GameClient game = new(client.UUID, text);
                }
                catch
                {

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




    }
}
