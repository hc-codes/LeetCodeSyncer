namespace LeetSyncer;
internal static class ConsoleExtension
{
    public static void WriteError(string error)
    {
        var color = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(error);
        Console.ForegroundColor = color;
    }
}
