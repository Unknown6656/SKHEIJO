using System.IO;
using System;

using Unknown6656.IO;
using System.Net;

namespace SKHEIJO
{
    public record Author(string Name, string? Email, string? Phone);

    public record Client(string Name, Guid UUID, string LastConnection);

    public record Configuration(Author Author, Client? Client)
    {
        public static Configuration Default { get; } = new(new("Unknown6656", null, null), null);


        public void WriteConfig(FileInfo file)
        {
            INIFile ini = new();
            INISection sec_auth = ini["author"];

            sec_auth["name"] = Author.Name;
            sec_auth["email"] = Author.Email ?? "";
            sec_auth["phone"] = Author.Phone ?? "";

            if (Client is { })
            {
                INISection sec_client = ini["client"];

                sec_client["conn"] = Client.LastConnection;
                sec_client["name"] = Client.Name;
                sec_client["guid"] = Client.UUID.ToString();
            }

            From.INI(ini).Compress(CompressionFunction.GZip).ToFile(file);
        }

        public static Configuration? TryReadConfig(FileInfo file)
        {
            try
            {
                static string? ne(string s) => string.IsNullOrEmpty(s) ? null : s;
                INIFile ini = From.File(file).Uncompress(CompressionFunction.GZip).ToINI();
                INISection sec_auth = ini["author"];
                Client? client = null;

                if (ini.HasSection("client"))
                {
                    INISection sec_client = ini["client"];

                    client = new(
                        sec_client["name"],
                        Guid.Parse(sec_client["guid"]),
                        sec_client["conn"]
                    );
                }

                return new(
                    new(
                        sec_auth["name"],
                        ne(sec_auth["email"]),
                        ne(sec_auth["phone"])
                    ),
                    client
                );
            }
            catch
            {
                return null;
            }
        }
    }
}
