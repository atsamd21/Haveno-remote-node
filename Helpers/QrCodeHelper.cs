using QRCoder;

namespace Manta.Remote.Helpers;

public static class QrCodeHelper
{
    public static void PrintExternalIpAddressAndPassword(string host, string password)
    {
        using var qrGenerator = new QRCodeGenerator();
        QRCodeData qrCodeData = qrGenerator.CreateQrCode(host + ";" + password, QRCodeGenerator.ECCLevel.Q);

        var qrCode = new AsciiQRCode(qrCodeData);
        string qrCodeAsAscii = qrCode.GetGraphic(1);

        Console.WriteLine(qrCodeAsAscii);
        Console.WriteLine($"Password: {password}");
        Console.WriteLine($"Address: {host}");
    }
}
