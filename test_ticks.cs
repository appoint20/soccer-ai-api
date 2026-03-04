using System;
public class Program {
    public static void Main() {
        long ticks = 1308835418112000000L;
        try {
            DateTimeOffset dto = new DateTimeOffset(ticks, TimeSpan.Zero);
            Console.WriteLine($"Date: {dto}");
        } catch (Exception ex) {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
