using Knapcode.TorSharp;
using Manta.Remote.Models;
using System.IO;
using System.Net;
using System.Net.Http.Json;

namespace Manta.Remote.Services;

public class TorService
{
    private readonly TorSharpSettings _settings;
    private string _torVersion = string.Empty;

    public TorService()
    {
        _settings = new TorSharpSettings
        {
            ZippedToolsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tor"),
            ExtractedToolsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tor"),
            PrivoxySettings = 
            { 
                Disable = true 
            },
            TorSettings = new TorSharpTorSettings
            {
                
            },
        };
    }

    public async Task EnsureTorInstalled()
    {
        Console.WriteLine("Checking Tor installation...");

        using var httpClient = new HttpClient();
        var fetcher = new TorSharpToolFetcher(_settings, httpClient);
        
        try
        {
            var update = await fetcher.CheckForUpdatesAsync();

            if (update.Tor.Status == ToolUpdateStatus.NoUpdateAvailable)
            {
                _torVersion = update.Tor.LocalVersion.ToString();

                Console.WriteLine("Tor installed and up to date");
                return;
            }

            if (update.Tor.Status == ToolUpdateStatus.NoLocalVersion)
            {
                Console.WriteLine("Tor not installed, will install now");

                await fetcher.FetchAsync();

                using var proxy = new TorSharpProxy(_settings);
                await proxy.ConfigureAndStartAsync();
                proxy.Stop();

                update = await fetcher.CheckForUpdatesAsync();
                _torVersion = update.Tor.LocalVersion.ToString();

                Console.WriteLine("Tor installed successfully");
            }
            else if (update.Tor.Status == ToolUpdateStatus.NewerVersionAvailable)
            {
                Console.WriteLine("Tor has available update, will update now");

                await fetcher.FetchAsync();

                using var proxy = new TorSharpProxy(_settings);
                await proxy.ConfigureAndStartAsync();
                proxy.Stop();

                update = await fetcher.CheckForUpdatesAsync();
                _torVersion = update.Tor.LocalVersion.ToString();

                Console.WriteLine("Tor updated successfully");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    public void SetupHiddenService()
    {
        string torFolderName;

        switch (_settings.OSPlatform)
        {
            case TorSharpOSPlatform.Linux:
                torFolderName = _settings.Architecture == TorSharpArchitecture.X64 ? "tor-linux64-" : "tor-linux32-";
                break;
            case TorSharpOSPlatform.Windows:
                torFolderName = _settings.Architecture == TorSharpArchitecture.X64 ? "tor-win64-" : "tor-win32-";
                break;
            default: throw new NotSupportedException("Platform not supported");
        }

        var torrcPath = Path.Combine(_settings.ExtractedToolsDirectory, $"{torFolderName}{_torVersion}", "data", "tor", "torrc");
        using var fileStream = File.Open(torrcPath, FileMode.Open, FileAccess.ReadWrite);

        using StreamReader reader = new(fileStream);
        string input = reader.ReadToEnd();

        string hiddenServiceParameter = "HiddenServicePort 2134 127.0.0.1:2134";
        string hiddenServiceDir = $"HiddenServiceDir {Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HiddenService")}";
        //string hiddenServiceParameter = "HiddenServicePort 9998 unix:/var/run/tor/my-website.sock;

        if (input.Contains(hiddenServiceParameter))
            return;

        using StreamWriter writer = new(fileStream);
        {
            writer.Write("\n" + hiddenServiceDir + "\n");
            writer.Write(hiddenServiceParameter + "\n");
        }

        writer.Close();
    }

    public async Task StartHiddenService()
    {
        var proxy = new TorSharpProxy(_settings);
        await proxy.ConfigureAndStartAsync();
    }

    public string GetOnionAddress()
    {
        string hostnameFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HiddenService", "hostname");
        using var fileStream = File.Open(hostnameFile, FileMode.Open, FileAccess.Read);
        using StreamReader reader = new(fileStream);
        return reader.ReadToEnd();
    }

    public async Task StartOutgoingTorProxy()
    {
        Console.WriteLine("Starting Tor");

        using var proxy = new TorSharpProxy(_settings);
        await proxy.ConfigureAndStartAsync();

        var handler = new HttpClientHandler
        {
            Proxy = new WebProxy(new Uri("socks5://localhost:" + _settings.TorSettings.SocksPort))
        };

        using (handler)
        using (var httpClient = new HttpClient(handler))
        {
            var result = await httpClient.GetFromJsonAsync<TorResponse>("https://check.torproject.org/api/ip");

            Console.WriteLine("IP address: " + result?.IP);
            Console.WriteLine("Is Tor: " + result?.IsTor);
        }

        proxy.Stop();
    }
}
