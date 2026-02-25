using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using soccer_gpt_application;
using soccer_gpt_infrastructure;
using soccer_gpt_worker.Worker;

if (!WorkerCommandParser.TryParse(args, out var command, out var parseError))
{
    Console.Error.WriteLine(parseError);
    Console.Error.WriteLine();
    Console.Error.WriteLine(WorkerCommandParser.HelpText);
    return 2;
}

if (command.IsHelp)
{
    Console.WriteLine(WorkerCommandParser.HelpText);
    return 0;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<WorkerCommandExecutor>();

using var host = builder.Build();
using var scope = host.Services.CreateScope();

var executor = scope.ServiceProvider.GetRequiredService<WorkerCommandExecutor>();
var exitCode = await executor.ExecuteAsync(command, CancellationToken.None);
return exitCode;
