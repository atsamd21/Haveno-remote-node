using Manta.Remote.Helpers;
using Manta.Remote.Services;

DaemonService daemonService = new();
TorService torService = new();

await daemonService.GetHavenoAsync();

await torService.EnsureTorInstalledAsync();

torService.SetupHiddenService();

await torService.StartHiddenServiceAsync();

var password = PasswordHelper.GetPassword();
var host = torService.GetOnionAddress();

QrCodeHelper.PrintExternalIpAddressAndPassword(host, password);

var daemonTask = Task.Run(() => daemonService.StartDaemonAsync(password));
var proxyTask = Task.Run(daemonService.StartReverseProxyAsync);

Task.WaitAny([proxyTask, daemonTask]);