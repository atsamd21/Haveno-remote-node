using System.Reflection;

namespace Manta.Remote.Models;

public static class AppConstants
{
    public static readonly string DaemonUrl;
    public static readonly string Network;
    public static readonly string HavenoAppName;
    public static readonly bool UseLocalhostForP2P;

    static AppConstants()
    {
        HavenoAppName = Assembly.GetExecutingAssembly()
           .GetCustomAttributes<AssemblyMetadataAttribute>()
           .FirstOrDefault(a => a.Key == "HavenoAppName")?.Value ?? string.Empty;

        Network = Assembly.GetExecutingAssembly()
           .GetCustomAttributes<AssemblyMetadataAttribute>()
           .FirstOrDefault(a => a.Key == "Network")?.Value ?? string.Empty;

        DaemonUrl = Assembly.GetExecutingAssembly()
           .GetCustomAttributes<AssemblyMetadataAttribute>()
           .FirstOrDefault(a => a.Key == "DaemonUrl")?.Value ?? string.Empty;

        UseLocalhostForP2P = bool.TryParse(Assembly.GetExecutingAssembly()
           .GetCustomAttributes<AssemblyMetadataAttribute>()
           .FirstOrDefault(a => a.Key == "UseLocalhostForP2P")?.Value, out var useLocalhostForP2P) && useLocalhostForP2P;
    }
}
