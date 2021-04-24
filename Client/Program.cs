using System.Diagnostics;
using System.Text;
using System;

using SKHEIJO;
using Unknown6656.IO;

Logger.Start();

try
{
    string? s;

    do
        s = Console.ReadLine();
    while (s is null);

    using GameClient client = new(Guid.NewGuid(), s);

    Console.WriteLine("type 'q' to exit");

    while (true)
    {
        string line = Console.ReadLine() ?? "";

        if (line == "q")
            break;
        else
            From.Bytes(await client.SendMessageAndWaitForReply(From.String(line))).ToString().Log();
    }

    client.Dispose();
}
catch (Exception ex)
{
    ex.Err();

    await Logger.Stop();

    if (Debugger.IsAttached)
    {
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey(true);
    }
}
