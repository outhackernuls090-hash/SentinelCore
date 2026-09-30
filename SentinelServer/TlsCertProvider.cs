using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SentinelServer;

public static class TlsCertProvider
{
    public static X509Certificate2 GetOrCreate(string dataDir)
    {
        var pfxPath = Path.Combine(dataDir, "sentinel-tls.pfx");
        if (File.Exists(pfxPath))
        {
            try
            {
                var existing = new X509Certificate2(pfxPath, (string?)null, X509KeyStorageFlags.Exportable);
                if (existing.NotAfter > DateTime.UtcNow.AddDays(7))
                    return existing;
            }
            catch
            {
            }
        }

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={Environment.MachineName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(Environment.MachineName);
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        req.CertificateExtensions.Add(sanBuilder.Build());

        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(3));
        var exported = cert.Export(X509ContentType.Pfx);
        Directory.CreateDirectory(dataDir);
        File.WriteAllBytes(pfxPath, exported);
        return new X509Certificate2(exported, (string?)null, X509KeyStorageFlags.Exportable);
    }

    public static string Fingerprint(X509Certificate2 cert) => Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256));
}
