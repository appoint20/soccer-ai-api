using System;
long ticks = 1308860006400000000L;
DateTimeOffset dto = new DateTimeOffset(ticks, TimeSpan.Zero);
Console.WriteLine($"Ticks {ticks} = {dto:yyyy-MM-dd HH:mm:ss}");
