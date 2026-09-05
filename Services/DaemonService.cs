using Manta.Remote.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace Manta.Remote.Services;

public class DaemonService
{
    private const int ProxyPort = 2134;

    private readonly string _os;
    private readonly string _daemonUrlFileName = "installed-daemon-url";
    private readonly string _basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppConstants.HavenoAppName);
    private readonly string _daemonPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppConstants.HavenoAppName, "daemon");
    private readonly string _dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppConstants.HavenoAppName, "data");

    public DaemonService()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _os = "windows";
        }
        else
        {
            _os = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos-" : "linux-";

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

            Match match = Regex.Match(output, @"version\s+""(?<version>\d+)");
            if (match.Success && Version.TryParse(match.Groups["version"].Value + (match.Groups["version"].Value.Contains('.') ? "" : ".0"), out var value) && value >= new Version(21, 0))
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

    private async Task FetchHaveno(string daemonPath, string daemonUrl, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();

        var jarUrl = $"{daemonUrl}/daemon-{_os}.jar";
        using var response = await client.GetAsync(jarUrl, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new Exception($"No daemon jar for {_os} at {jarUrl}. Ask the network operator to publish daemon-{_os}.jar, or set DaemonUrl to a release which has it.");

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Could not download {jarUrl}: {(int)response.StatusCode} {response.ReasonPhrase}");

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        using MemoryStream memoryStream = new(bytes);

        using var daemonFileStream = File.Create(Path.Combine(daemonPath, "daemon.jar"));
        await memoryStream.CopyToAsync(daemonFileStream, cancellationToken);

        daemonFileStream.Close();
        await File.WriteAllTextAsync(Path.Combine(daemonPath, _daemonUrlFileName), daemonUrl, cancellationToken);

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

    private string? GetInstalledDaemonUrl(string daemonPath)
    {
        try
        {
            using var fileStream = File.Open(Path.Combine(daemonPath, _daemonUrlFileName), FileMode.Open, FileAccess.ReadWrite);
            using var reader = new StreamReader(fileStream);

            var currentIntalledDaemonUrl = reader.ReadToEnd();
            reader.Close();

            return currentIntalledDaemonUrl;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, string[] exclude)
    {
        var sourceDirectoryInfo = new DirectoryInfo(sourceDirectory);
        if (!sourceDirectoryInfo.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectoryInfo.FullName}");

        var subDirectories = sourceDirectoryInfo.GetDirectories();

        Directory.CreateDirectory(destinationDirectory);

        foreach (var fileInfo in sourceDirectoryInfo.GetFiles())
        {
            if (exclude.Contains(fileInfo.FullName))
                continue;

            string targetFilePath = Path.Combine(destinationDirectory, fileInfo.Name);
            fileInfo.CopyTo(targetFilePath);
        }

        foreach (var subDirectoryInfo in subDirectories)
        {
            if (exclude.Contains(subDirectoryInfo.FullName))
                continue;

            string newDestinationDirectory = Path.Combine(destinationDirectory, subDirectoryInfo.Name);
            CopyDirectory(subDirectoryInfo.FullName, newDestinationDirectory, exclude);
        }
    }

    private static void ZipDirectory(string sourceDirectory, string zipPath, string[] exclude)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        var files = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            if (exclude.Contains(file) || file == zipPath)
                continue;

            var entryName = Path.GetRelativePath(sourceDirectory, file);
            zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }
    }

    public async Task GetHavenoAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine("Checking Haveno installation");

        Directory.CreateDirectory(_daemonPath);

        // If previous copy failed for some reason
        var tmpPath = Path.Combine(_basePath, "tmp");
        if (Directory.Exists(tmpPath))
            Directory.Delete(tmpPath, true);

        // If backup has not been done and if there are files/dirs to backup 
        if (!Directory.Exists(_dataPath) && (Directory.GetDirectories(_basePath).Length > 1))
        {
            Console.WriteLine("Moving Haveno user data to new directory...");

            // Just in case
            Console.WriteLine("Backing up...");
            var zipPath = Path.Combine(_basePath, $"backup-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.zip");
            ZipDirectory(_basePath, zipPath, [ _daemonPath ]);
            Console.WriteLine("Backup done");

            Directory.CreateDirectory(tmpPath);

            CopyDirectory(_basePath, tmpPath, [_daemonPath, _dataPath, tmpPath, zipPath ]);
            Directory.Move(tmpPath, _dataPath);

            // Remove 
            var files = Directory.GetFiles(_basePath);
            foreach (var file in files)
            {
                if (file == _daemonPath || file == _dataPath || file.Contains(".zip"))
                    continue;

                File.Delete(file);
            }

            var directories = Directory.GetDirectories(_basePath);
            foreach (var directory in directories)
            {
                if (directory == _daemonPath || directory == _dataPath || directory.Contains(".zip"))
                    continue;

                Directory.Delete(directory, true);
            }

            Console.WriteLine("Done moving data");
        }

        var currentIntalledDaemonUrl = GetInstalledDaemonUrl(_daemonPath);

        if (string.IsNullOrEmpty(currentIntalledDaemonUrl))
        {
            Console.WriteLine("Haveno daemon not installed, will install now...");

            await FetchHaveno(_daemonPath, AppConstants.DaemonUrl, cancellationToken);

            Console.WriteLine("Haveno daemon finished installing");
        }
        else
        {
            if (AppConstants.DaemonUrl != currentIntalledDaemonUrl)
            {
                Console.WriteLine("New Haveno version found. Updating Haveno daemon...");

                Directory.Delete(_daemonPath, true);
                Directory.CreateDirectory(_daemonPath);

                await FetchHaveno(_daemonPath, AppConstants.DaemonUrl, cancellationToken);

                Console.WriteLine("Finished updating");
            }
            else
            {
                Console.WriteLine("Haveno daemon is up to date");
            }
        }

        if (!IsJavaInstalled())
        {
            throw new Exception("Java not installed. Make sure to install Java 21");
        }
    }

    public IHost CreateReverseProxy()
    {
        var builder = Host.CreateDefaultBuilder().ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.ConfigureKestrel(serverOptions =>
            {
                serverOptions.Listen(IPAddress.Loopback, ProxyPort, listenOptions =>
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
                                Address = "http://127.0.0.1:3201" // the daemon api binds to ipv4 loopback by default
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

        return builder.Build();
    }

    public async Task StartDaemonAsync(string password, Action<string> onOnionAddressFound, CancellationToken cancellationToken)
    {
        var currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
        if (string.IsNullOrEmpty(currentDirectory))
            throw new Exception();

        // use ArgumentList so paths with spaces (e.g. ~/Library/Application Support) are quoted correctly
        ProcessStartInfo startInfo = new()
        {
            FileName = "java",
            ArgumentList =
            {
                "-Dstdout.encoding=UTF-8",
                "-jar",
                Path.Combine(_daemonPath, "daemon.jar"),
                $"--baseCurrencyNetwork={AppConstants.Network}",
                "--useLocalhostForP2P=false",
                "--useDevPrivilegeKeys=false",
                "--nodePort=9999",
                $"--appDataDir={_dataPath}",
                $"--appName={AppConstants.HavenoAppName}",
                $"--apiPassword={password}",
                "--apiPort=3201",
                "--apiHiddenService=true",
                // Allow the mobile app to unlock the account remotely after a restart.
                "--apiHiddenServiceBeforeLogin=true",
                $"--apiHiddenServicePort={ProxyPort}",
                "--passwordRequired=false",
                "--disableRateLimits=true",
                "--useNativeXmrWallet=false",
            },

            WorkingDirectory = currentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(startInfo) ?? throw new Exception("Could not start Haveno daemon");
        using var outputCancellation = new CancellationTokenSource();
        var outputTask = ForwardDaemonOutputAsync(process.StandardOutput, outputCancellation.Token);
        try
        {
            var hostnameFile = Path.Combine(_dataPath, AppConstants.Network.ToLowerInvariant(), "tor", "hiddenservice", "api", "hostname");
            bool addressFound = false;
            Console.WriteLine("Waiting for the daemon's API onion address...");

            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (outputTask.IsCompleted)
                    await outputTask;

                var hostname = ReadHostname(hostnameFile);
                if (Regex.IsMatch(hostname, @"\A[a-z2-7]{56}\.onion\z") && !process.HasExited)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // The persisted address is valid across restarts; finding it does not imply Tor is ready.
                    Console.WriteLine("Tor startup may still be in progress.");
                    onOnionAddressFound(hostname);
                    addressFound = true;
                    break;
                }

                await Task.Delay(1000, cancellationToken);
            }

            var exitTask = process.WaitForExitAsync(cancellationToken);
            var completed = await Task.WhenAny(exitTask, outputTask);
            await completed;
            await exitTask;

            if (process.ExitCode != 0 && !cancellationToken.IsCancellationRequested)
                throw new Exception($"Haveno daemon exited with code {process.ExitCode}. Check the daemon output above.");

            if (!addressFound && !cancellationToken.IsCancellationRequested)
                throw new Exception("Haveno daemon stopped before its API onion address was available. Check the daemon output above.");
        }
        finally
        {
            try
            {
                await StopDaemonAsync(process);
            }
            finally
            {
                try
                {
                    // Drain final diagnostics, but do not wait indefinitely for a descendant holding stdout open.
                    await outputTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    outputCancellation.Cancel();
                    Console.Error.WriteLine("Stopped waiting for the daemon's output pipe to close.");
                }
            }
        }
    }

    private static async Task ForwardDaemonOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        // Use the same console writer so forwarded stdout cannot split the QR graphic.
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
            Console.WriteLine(line);
    }

    private static string ReadHostname(string path)
    {
        try
        {
            // Allow the daemon to write or replace the file while it is being read, including on Windows.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Trim();
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static async Task StopDaemonAsync(Process process)
    {
        if (process.HasExited)
            return;

        Console.WriteLine("Stopping Haveno daemon...");
        // Windows delivers the console event to Java too. On Unix, also handle SIGTERM sent only to this app.
        if (!OperatingSystem.IsWindows())
            kill(process.Id, 15);

        try
        {
            // Allow the daemon's four-minute shutdown watchdog to run before forcing termination.
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(5));
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("Haveno daemon did not stop within five minutes; terminating its process tree.");
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
            }
            await process.WaitForExitAsync();
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int signal);
}
