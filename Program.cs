using Manta.Remote.Helpers;
using Manta.Remote.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

DaemonService daemonService = new();
using var proxy = daemonService.CreateReverseProxy();

try
{
    // Bind before starting Java so a second node fails without touching daemon data.
    await proxy.StartAsync();
    var stopping = proxy.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;

    try
    {
        await daemonService.GetHavenoAsync(stopping);
        var password = PasswordHelper.GetPassword();

        await daemonService.StartDaemonAsync(password,
            host => QrCodeHelper.PrintExternalIpAddressAndPassword(host, password), stopping);
    }
    catch (OperationCanceledException) when (stopping.IsCancellationRequested)
    {
        // The host handles Ctrl+C and SIGTERM; the daemon finishes shutting down first.
    }
    finally
    {
        await proxy.StopAsync();
    }

    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
