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
        public Configuration Configuration { get; private set; }


        public ConnectWindow(Configuration configuration)
        {
            InitializeComponent();

            Configuration = configuration;
            Loaded += ConnectWindow_Loaded;
        }

        private void ConnectWindow_Loaded(object sender, RoutedEventArgs e)
        {
            lb_author.Text = Configuration.Author.Name;
            tbl_contact.Inlines.Clear();

            if (Configuration.Author.Email is string mail)
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

                if (Configuration.Author.Phone is string)
                    tbl_contact.Inlines.Add(new Run(" or "));
            }

            if (Configuration.Author.Phone is string phone)
                tbl_contact.Inlines.Add(new Hyperlink(new Run(phone))
                {
                    NavigateUri = new("tel://" + phone)
                });
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
