using Manta.Remote.Helpers;
using Manta.Remote.Services;

DaemonService daemonService = new();
TorService torService = new();

await torService.EnsureTorInstalled();

torService.SetupHiddenService();

await torService.StartHiddenService();

await daemonService.GetHaveno();

var password = PasswordHelper.GetPassword();
var host = torService.GetOnionAddress();

QrCodeHelper.PrintExternalIpAddressAndPassword(host, password);

var daemonTask = Task.Run(() => daemonService.StartDaemon(password));
var proxyTask = Task.Run(daemonService.StartReverseProxy);

Task.WaitAny([proxyTask, daemonTask]);