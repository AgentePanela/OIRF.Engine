using System;
using System.Collections.Generic;

/// <summary>
/// Use as your log service in any side.
/// </summary>
public static class Log
{
    internal static bool ExceptOnWarn = false;

    /// <summary>
    /// Fired for every log line written, after the terminal write it
    /// </summary>
    public static event Action<string?, string, ConsoleColor, ConsoleColor?>? OnLog;

    public static void Debug(object? log) => Write(log, ConsoleColor.Cyan, "DEBUG");
    public static void Warn(object? log) => Write(log, ConsoleColor.Yellow, "WARN ", true);
    public static void Error(object? log) => Write(log, ConsoleColor.Red, "ERROR", true);
    public static void Blank(object? log, ConsoleColor? overrideColor = default) 
        => Write(log, ConsoleColor.White, null, false, overrideColor);
    
    public static void Clowny(object? log)
    {
        if (log is string s)
        {
            var rnd = new Random();
            var result = new char[s.Length];

            for (int i = 0; i < s.Length; i++)
            {
                result[i] = rnd.Next(2) == 0
                    ? char.ToLower(s[i])
                    : char.ToUpper(s[i]);
            }
            log = new string(result);
        }

        Write(log, ConsoleColor.DarkMagenta, "CLOWN");
    }

    static private void Write(object? log, ConsoleColor color, string? prefix, bool warningOrError = false, ConsoleColor? levelColor = default)
    {        
        if (prefix is not null)
        {
            Console.Write("[");
            Console.ForegroundColor = color;
            Console.Write(prefix);
            Console.ResetColor();
            Console.Write("] ");
        }

        string output;

        if (log is string s)
            output = s;
        else
            output = Newtonsoft.Json.JsonConvert.SerializeObject(log, Newtonsoft.Json.Formatting.Indented);

        if (levelColor is null)
            levelColor = LevelColor.TryGetValue(prefix ?? "", out var found) ? found : null;

        var contentColor = levelColor ?? ConsoleColor.Gray;
        Console.ForegroundColor = contentColor;

        Console.WriteLine(output);
        Console.ResetColor();
        OnLog?.Invoke(prefix, output, color, levelColor);
        if (warningOrError && ExceptOnWarn)
            throw new Exception(output);
    }

    private static readonly Dictionary<string, ConsoleColor> LevelColor = new()
    {
        //{ "Debug", ConsoleColor.Cyan },
        { "WARN ",  ConsoleColor.Yellow },
        { "ERROR", ConsoleColor.Red },
        { "CLOWN", ConsoleColor.DarkMagenta }
    };
}
