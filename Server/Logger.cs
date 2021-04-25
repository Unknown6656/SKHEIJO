using System.Collections.Concurrent;
using System.Threading.Tasks;
using System;

using Fleck;

namespace SKHEIJO
{
    public enum LogSeverity
    {
        Debug,
        Info,
        Warning,
        Error,
    }

    public enum LogSource
    {
        Unknown,
        Log,
        Server,
        Client,
        WebServer,
        UI,
        Game,
    }

    public static class Logger
    {
        private static readonly ConcurrentQueue<(DateTime time, LogSource source, LogSeverity severity, string msg)> _queue = new();
        private static readonly ConsoleColor _initial_color = ConsoleColor.Gray;
        private static readonly object _mutex = new();
        private static LogSeverity _level = LogSeverity.Debug;


        public static bool IsRunning { get; private set; }

        public static LogSeverity MinimumSeverityLevel
        {
            get => _level;
            set
            {
                _level = value;

                FleckLog.Level = value switch
                {
                    LogSeverity.Info => LogLevel.Info,
                    LogSeverity.Warning => LogLevel.Warn,
                    LogSeverity.Error => LogLevel.Error,
                    _ => LogLevel.Debug,
                };
            }
        }


        static Logger()
        {
            _initial_color = Console.ForegroundColor;
            MinimumSeverityLevel = LogSeverity.Debug;

            FleckLog.LogAction = LogWebsocket;
        }

        public static void Start() => Task.Factory.StartNew(async delegate
        {
            if (IsRunning)
                return;

            "Logger has been started.".Log(LogSource.Log);

            IsRunning = true;

            while (IsRunning)
                if (!Flush())
                    await Task.Delay(10);

            Flush();

            Console.ForegroundColor = _initial_color;
        });

        public static async Task Stop()
        {
            if (!IsRunning)
                return;

            "Logger has been stopped.".Log(LogSource.Log);

            IsRunning = false;

            while (!_queue.IsEmpty)
                await Task.Delay(1);
        }

        private static bool Flush()
        {
            bool any = false;

            while (_queue.TryDequeue(out var item))
            {
                any = true;

                if (item.severity >= MinimumSeverityLevel)
                    lock (_mutex)
                    {
                        (ConsoleColor color, string prefix) = item.severity switch
                        {
                            LogSeverity.Info => (ConsoleColor.Cyan, "INFO"),
                            LogSeverity.Warning => (ConsoleColor.Yellow, "WARN"),
                            LogSeverity.Error => (ConsoleColor.Red, "ERR."),
                            _ => (ConsoleColor.White, "DBG."),
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
                        Console.Write("][");
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.Write(item.source);
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write(']');
                        Console.CursorLeft = 36;
                        Console.ForegroundColor = color;
                        Console.WriteLine(item.msg);
                    }
            }

            return any;
        }

        public static void Warn(this object? obj, LogSource source) => Log(obj, source, LogSeverity.Warning);

        public static void Err(this object? obj, LogSource source) => Log(obj, source, LogSeverity.Error);

        public static void Info(this object? obj, LogSource source) => Log(obj, source, LogSeverity.Info);

        public static void Log(this object? obj, LogSource source) => Log(obj, source, LogSeverity.Debug);

        public static void Log(this object? obj, LogSource source, LogSeverity severity) =>
            _queue.Enqueue((DateTime.Now, source, severity, obj as string ?? obj?.ToString() ?? ""));

        private static void LogWebsocket(LogLevel level, string message, Exception? ex)
        {
            LogSeverity severity = level switch
            {
                LogLevel.Info => LogSeverity.Info,
                LogLevel.Warn => LogSeverity.Warning,
                LogLevel.Error => LogSeverity.Error,
                _ => LogSeverity.Debug,
            };

            Log(message, LogSource.WebServer, severity);

            if (ex is { })
                Log(ex, LogSource.WebServer, severity);
        }
    }
}
