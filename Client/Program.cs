using System.Diagnostics;
using System.Text;
using System;

using SKHEIJO;


try
{
    string? s;

    do
        s = Console.ReadLine();
    while (s is null);

    using GameClient client = new(s);

    Console.WriteLine("press ENTER to exit");
    Console.ReadLine();

    client.Dispose();
}
catch (Exception? ex)
{
    StringBuilder sb = new();

    while (ex is Exception)
    {
        sb.Insert(0, $"[{ex.GetType()}] {ex.Message}:\n{ex.StackTrace}\n");

        ex = ex.InnerException;
    }

    Console.WriteLine(sb);

    if (Debugger.IsAttached)
    {
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey(true);
    }
}
