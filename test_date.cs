using System;
Console.WriteLine($"Now: {DateTimeOffset.UtcNow}");
Console.WriteLine($"Date: {DateTimeOffset.UtcNow.Date}");
Console.WriteLine($"Local Ticks: {new DateTimeOffset(DateTimeOffset.UtcNow.Date).Ticks}");
Console.WriteLine($"UTC Ticks: {new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).Ticks}");
