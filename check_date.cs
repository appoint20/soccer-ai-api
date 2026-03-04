using System;
long ticks = 1308835418112000000L;
DateTimeOffset dto = new DateTimeOffset(ticks, TimeSpan.Zero);
Console.WriteLine($"Date: {dto}");
