using DotNetEnv;
using MittwaldDdns;

Env.Load();
return await CliApplication.Create().RunAsync(args);