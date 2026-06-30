using System;
using System.Collections.Generic;

/// <summary>
/// Use as your log service in any side.
/// </summary>
public static class Log
{
    internal static bool ExceptOnWarn = false;
    public static void Debug(object? log) => Write(log, 11, "DEBUG");
    public static void Warn(object? log) => Write(log, 14, "WARN ", true);
    public static void Error(object? log) => Write(log, 12, "ERROR", true);
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

        Write(log, 5, "CLOWN");
    }

    static private void Write(object? log, int icolor, string prefix, bool warningOrError = false)
    {
        var color = (ConsoleColor)icolor;
        
        Console.Write("[");
        Console.ForegroundColor = color;
        Console.Write(prefix);
        Console.ResetColor();

        string output;

        if (log is string s)
            output = s;
        else
            output = Newtonsoft.Json.JsonConvert.SerializeObject(log, Newtonsoft.Json.Formatting.Indented);

        Console.Write("] ");

        if (LevelColor.TryGetValue(prefix, out var contentColor))
            Console.ForegroundColor = contentColor;

        Console.WriteLine(output);
        Console.ResetColor();
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
