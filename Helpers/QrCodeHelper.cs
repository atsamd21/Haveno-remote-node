using QRCoder;

namespace Manta.Remote.Helpers;

public static class QrCodeHelper
{
    public static async Task PrintExternalIpAddressAndPassword(string password)
    {
        using var client = new HttpClient();

        string ipAddress = await client.GetStringAsync("https://ipinfo.io/ip");

        if (string.IsNullOrEmpty(ipAddress))
        {
            Console.WriteLine("Could not get external IP address!");
            return;
        }

        using var qrGenerator = new QRCodeGenerator();
        QRCodeData qrCodeData = qrGenerator.CreateQrCode(ipAddress + ";" + password, QRCodeGenerator.ECCLevel.Q);

        var qrCode = new AsciiQRCode(qrCodeData);
        string qrCodeAsAscii = qrCode.GetGraphic(1);

        Console.WriteLine(qrCodeAsAscii);
        Console.WriteLine($"Password: {password}");
        Console.WriteLine($"Address: {ipAddress}");
    }
}
