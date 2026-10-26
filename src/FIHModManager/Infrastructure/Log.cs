namespace FIHModManager.Infrastructure;

public static class Log
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FIHModManager");

    private static readonly string LogPath = Path.Combine(DataDir, "manager.log");
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string context, Exception ex) => Write("ERROR", $"{context}: {ex}");

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level} {message}{Environment.NewLine}");
        }
    }
}
