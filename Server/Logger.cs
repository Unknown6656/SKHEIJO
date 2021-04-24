using System.Collections.Concurrent;
using System.Threading.Tasks;
using System;

namespace SKHEIJO
{
    public enum LogSeverity
    {
        Debug,
        OK,
        Warning,
        Error,
    }

    public static class Logger
    {
        private static readonly ConcurrentQueue<(DateTime time, LogSeverity severity, string msg)> _queue = new();
        private static readonly ConsoleColor _initial_color;

        public static LogSeverity MinimumSeverityLevel { get; set; } = LogSeverity.Debug;
        public static bool IsRunning { get; private set; }


        static Logger() => _initial_color = Console.ForegroundColor;

        public static void Start() => Task.Factory.StartNew(async delegate
        {
            Log("Logger has been started.");

            IsRunning = true;

            while (IsRunning)
                if (!Flush())
                    await Task.Delay(10);

            Flush();

            Console.ForegroundColor = _initial_color;
        });

        private static bool Flush()
        {
            bool any = false;

            while (_queue.TryDequeue(out var item))
            {
                any = true;

                if (item.severity >= MinimumSeverityLevel)
                {
                    (ConsoleColor color, string prefix) = item.severity switch
                    {
                        LogSeverity.OK => (ConsoleColor.Green, " OK "),
                        LogSeverity.Warning => (ConsoleColor.Yellow, "WARN"),
                        LogSeverity.Error => (ConsoleColor.Red, "ERR."),
                        _ => (ConsoleColor.White, "    "),
                    };

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write('[');
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write(item.time.ToString("HH:mm:ss.ffffff"));
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("][");
                    Console.ForegroundColor = color;
                    Console.Write(prefix);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("]  ");
                    Console.ForegroundColor = color;
                    Console.WriteLine(item.msg);
                }
            }

            return any;
        }

        public static async Task Stop()
        {
            Log("Logger has been stopped.");

            IsRunning = false;

            while (!_queue.IsEmpty)
                await Task.Delay(1);
        }

        public static void Warn(this object? obj) => Log(obj, LogSeverity.Warning);

        public static void Err(this object? obj) => Log(obj, LogSeverity.Error);

        public static void Ok(this object? obj) => Log(obj, LogSeverity.OK);

        public static void Log(this object? obj) => Log(obj, LogSeverity.Debug);

        public static void Log(this object? obj, LogSeverity severity) => _queue.Enqueue((DateTime.Now, severity, obj as string ?? obj?.ToString() ?? ""));
    }
}
