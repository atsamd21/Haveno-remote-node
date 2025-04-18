using Manta.Remote.Services;
using Microsoft.Extensions.Configuration;

DaemonService daemonService = new();

await daemonService.GetHaveno();

IConfigurationRoot config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

var password = config["password"];
if (password is null)
{
    password = Guid.NewGuid().ToString();
    config["password"] = password;
}

var daemonTask = Task.Run(() => daemonService.StartDaemon(password));
var proxyTask = Task.Run(daemonService.StartReverseProxy);

Task.WaitAny([proxyTask, daemonTask]);