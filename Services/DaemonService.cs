using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using System.Net;
using Yarp.ReverseProxy.Forwarder;
using Microsoft.Extensions.Logging;

namespace Manta.Remote.Services;

public enum HavenoInstallationStatus
{
    None,
    NotInstalled,
    InstalledLatest,
    InstalledOutOfDate
}

public class DaemonService
{
    // TODO give user options to choose network
    private string[] _havenoRepos = ["atsamd21/haveno", "haveno-dex/haveno"];
    private string? _currentHavenoVersion;
    private string _os;
    private HavenoInstallationStatus _havenoInstallationStatus;

    public DaemonService()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _os = "windows";
        }
        else
        {
            _os = "linux-";

            if (RuntimeInformation.OSArchitecture.ToString() == "X64")
            {
                _os += "x86_64";
            }
            else
            {
                _os += "aarch64";
            }
        }
    }

    private bool IsJavaInstalled()
    {
        Console.WriteLine("Checking java installation");

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "java",
                Arguments = "-version",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                throw new Exception("Could not start process");
            }

            string output = process.StandardError.ReadToEnd();

            process.WaitForExit();

            Match match = Regex.Match(output, @"version\s+""(?<version>\d+)\.");
            if (match.Success && match.Groups["version"].Value == "21")
            {
                Console.WriteLine("Java installed");
                return true;
            }
            else
            {
                Console.WriteLine("Java not installed");
                return false;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }

    private async Task FetchHaveno(string daemonPath, string selectedRepo, string latestVersion)
    {
        using var client = new HttpClient();

        //var bytes = await client.GetByteArrayAsync($"https://github.com/{selectedRepo}/archive/refs/tags/v{latestVersion}.zip");
        var bytes = await client.GetByteArrayAsync($"https://github.com/{selectedRepo}/releases/download/v{latestVersion}/{_os}.zip");

        using MemoryStream memoryStream = new(bytes);

        ZipFile.ExtractToDirectory(memoryStream, daemonPath);

        using var fileStream = File.Open(Path.Combine(daemonPath, "version"), FileMode.OpenOrCreate, FileAccess.ReadWrite);
        using var writer = new StreamWriter(fileStream);
        writer.Write(latestVersion);
        writer.Close();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "chmod",
                Arguments = $"+x haveno-daemon",
                WorkingDirectory = Path.Combine(daemonPath)
            };

            var process = Process.Start(startInfo);
            process!.WaitForExit();
        }
    }

    public async Task GetHavenoAsync()
    {
        Console.WriteLine("Checking Haveno installation");

        var daemonPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "daemon");
        
        Directory.CreateDirectory(daemonPath);

        if (Directory.GetFiles(daemonPath).Any(x => x.Contains("version")))
        {
            using var fileStream = File.Open(Path.Combine(daemonPath, "version"), FileMode.OpenOrCreate, FileAccess.ReadWrite);
            using var reader = new StreamReader(fileStream);

            _currentHavenoVersion = await reader.ReadToEndAsync();
            reader.Close();
        }
        else
        {
            _havenoInstallationStatus = HavenoInstallationStatus.NotInstalled;
        }

        var selectedRepo = _havenoRepos[0];

        // Could also get a list of all releases and let user choose
        using var client = new HttpClient();
        var response = await client.GetAsync($"https://github.com/{selectedRepo}/releases/latest");

        // Are all prepended with "v"?
        var latestVersion = response.RequestMessage?.RequestUri?.ToString().Split("tag/v").ElementAt(1);
        if (latestVersion is null)
            throw new Exception("Could not parse latest version");

        if (_havenoInstallationStatus == HavenoInstallationStatus.NotInstalled)
        {
            Console.WriteLine("Haveno daemon not installed, will install now");

            await FetchHaveno(daemonPath, selectedRepo, latestVersion);

            Console.WriteLine("Haveno daemon finished installing");
        }
        else
        {
            if (string.IsNullOrEmpty(_currentHavenoVersion))
                throw new Exception("_currentHavenoVersion was null");

            if (Version.Parse(latestVersion) > Version.Parse(_currentHavenoVersion))
            {
                _havenoInstallationStatus = HavenoInstallationStatus.InstalledOutOfDate;

                Console.WriteLine($"There is a new version available: v{latestVersion}. Current version is: v{_currentHavenoVersion}. Would you like to update? Enter [y] or [yes], default is no");

                var input = Console.ReadLine();
                if (!string.IsNullOrEmpty(input))
                {
                    input = input.Trim().ToLower();

                    if (input == "y" || input == "yes")
                    {
                        Console.WriteLine("Updating Haveno daemon...");

                        // Data is saved in appdata/user folders so this is fine
                        Directory.Delete(daemonPath, true);

                        await FetchHaveno(daemonPath, selectedRepo, latestVersion);
                    }
                }
            }
            else
            {
                _havenoInstallationStatus = HavenoInstallationStatus.InstalledLatest;
                Console.WriteLine("Haveno daemon up to date");
            }
        }

        if (!IsJavaInstalled())
        {
            throw new Exception("Java not installed. Make sure to install Java 21");
        }
    }

    public async Task StartReverseProxyAsync()
    {
        var builder = Host.CreateDefaultBuilder().ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.ConfigureKestrel(serverOptions =>
            {
                serverOptions.Listen(IPAddress.Any, 2134, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http1;
                });
            });

            webBuilder.ConfigureServices(services =>
            {
                services.AddCors(options =>
                {
                    options.AddPolicy("customPolicy", builder =>
                    {
                        builder.AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding");
                    });
                });

                services.AddReverseProxy()
                .LoadFromMemory(
                [
                    new RouteConfig
                    {
                        RouteId = "grpcRoute",
                        ClusterId = "havenoCluster",
                        Match = new RouteMatch
                        {
                            Path = "{**catch-all}",
                        }
                    }
                ], 
                [
                    new ClusterConfig
                    {
                        ClusterId = "havenoCluster",
                        Destinations = new Dictionary<string, DestinationConfig>
                        {
                            ["haveno"] = new DestinationConfig
                            {
                                Address = "http://localhost:3201"
                            }
                        },
                        HttpRequest = new ForwarderRequestConfig
                        {
                            Version = new Version(2, 0),
                            VersionPolicy = HttpVersionPolicy.RequestVersionExact
                        }
                    }
                ]);
            });

            webBuilder.Configure(app =>
            {
                app.UseRouting();
                app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapReverseProxy().EnableGrpcWeb();
                });
            });
        });

        await builder.Build().RunAsync();
    }

    public async Task StartDaemonAsync(string password)
    {
        var currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
        if (string.IsNullOrEmpty(currentDirectory))
            throw new Exception();

        var procesess = Process.GetProcesses();
        int i = 0;
        foreach (var p in procesess)
        {
            if (p.ProcessName.CompareTo("Manta.Remote") == 0)
            {
                i++;
            }
        }

        if (i > 1)
        {
            throw new Exception("Manta.Remote is already running");
        }

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        var daemonPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "daemon");

        ProcessStartInfo startInfo = new()
        {
            FileName = isWindows ? Path.Combine(daemonPath, "haveno-daemon.bat") : Path.Combine(daemonPath, "haveno-daemon"),
            Arguments = "--baseCurrencyNetwork=XMR_STAGENET " +
                        "--useLocalhostForP2P=false " +
                        "--useDevPrivilegeKeys=false " +
                        "--nodePort=9999 " +
                        "--appName=haveno-XMR_STAGENET " +
                        $"--apiPassword={password} " +
                        "--apiPort=3201 " +
                        "--passwordRequired=false " +
                        "--disableRateLimits " +
                        "--useNativeXmrWallet=false",

            WorkingDirectory = currentDirectory
        };

        var process = Process.Start(startInfo);

        if (process is null)
            throw new Exception("process was null");

        process.Exited += (sender, e) =>
        {
            Console.WriteLine("Haveno daemon exited");
        };

        Console.CancelKeyPress += (sender, e) =>
        {
            Console.WriteLine("SIGINT received");

            process.StandardInput.Close();

            e.Cancel = false;
        };

        await process.WaitForExitAsync();
    }
}
