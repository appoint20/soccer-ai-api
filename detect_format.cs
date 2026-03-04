using System;
public class Program {
    public static void Main() {
        long val = 1308835418112000000L;
        // Try DateTime.FromBinary
        try {
            DateTime dt = DateTime.FromBinary(val);
            Console.WriteLine($"FromBinary: {dt}");
        } catch {}

        // Try Ticks
        try {
            DateTime dt = new DateTime(val);
            Console.WriteLine($"Ticks: {dt}");
        } catch {}

        // Try Unix Milliseconds
        try {
            DateTimeOffset dto = DateTimeOffset.FromUnixTimeMilliseconds(val / 1000000); // Guessing scaling
             Console.WriteLine($"UnixMsScaled: {dto}");
        } catch {}
        
        // Try the EF Core SQLite default format: 
        // Some versions of EF Core store DateTimeOffset as: ((Ticks - MinTicks) << 15) | Offset
        // No, that's complex.
    }
}
