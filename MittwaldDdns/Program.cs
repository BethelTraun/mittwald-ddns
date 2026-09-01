using DotNetEnv;
using MittwaldDdns;

Env.Load();

if (LoginCommand.TryCreate(args, out var loginCommand) && loginCommand is not null)
{
    return await loginCommand.RunAsync();
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
return 0;
