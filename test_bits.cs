using System;
public class Program {
    public static void Main() {
        long value = 1308835418112000000L;
        // Try various bit shifts
        for (int i = 0; i < 20; i++) {
            long ticks = value >> i;
            if (ticks < DateTimeOffset.MaxValue.Ticks && ticks > DateTimeOffset.MinValue.Ticks) {
                try {
                    DateTimeOffset dto = new DateTimeOffset(ticks, TimeSpan.Zero);
                    if (dto.Year > 2000 && dto.Year < 2100) {
                        Console.WriteLine($"Shift {i}: {dto} (Ticks: {ticks})");
                    }
                } catch {}
            }
        }
    }
}
