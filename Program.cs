using Manta.Remote.Helpers;
using Manta.Remote.Services;

DaemonService daemonService = new();

await daemonService.GetHaveno();

var password = PasswordHelper.GetPassword();

var daemonTask = Task.Run(() => daemonService.StartDaemon(password));
var proxyTask = Task.Run(daemonService.StartReverseProxy);

Task.WaitAny([proxyTask, daemonTask]);