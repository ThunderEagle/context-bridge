using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ContextBridge.Infrastructure.Security;

public static class CertificateManager
{
    private const string SubjectName = "CN=ContextBridge";

    /// <summary>
    /// Generates a self-signed TLS certificate for localhost, installs it to LocalMachine\My
    /// (accessible to the Windows Service account) and trusts it via LocalMachine\Root.
    /// </summary>
    public static X509Certificate2 GenerateAndInstall()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(SubjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        req.CertificateExtensions.Add(san.Build());

        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // Server Authentication

        // Start 1 day in the past to absorb any clock-skew between machines
        using var ephemeral = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));

        // Export and reimport with MachineKeySet so LocalSystem (service account) can load the private key
        var pfx = ephemeral.Export(X509ContentType.Pfx);
        var cert = X509CertificateLoader.LoadPkcs12(pfx, password: null,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);

        using (var store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
        {
            store.Open(OpenFlags.ReadWrite);
            store.Add(cert);
        }

        using (var rootStore = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
        {
            rootStore.Open(OpenFlags.ReadWrite);
            rootStore.Add(cert);
        }

        return cert;
    }

    public static X509Certificate2? FindByThumbprint(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var results = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        return results.Count > 0 ? results[0] : null;
    }

    /// <summary>
    /// Returns true if a cert with the given thumbprint exists in LocalMachine\My
    /// and has at least <paramref name="minDaysRemaining"/> days of validity left.
    /// </summary>
    public static bool IsValid(string? thumbprint, int minDaysRemaining = 30)
    {
        if (string.IsNullOrWhiteSpace(thumbprint)) { return false; }
        var cert = FindByThumbprint(thumbprint);
        return cert is not null && cert.NotAfter > DateTime.UtcNow.AddDays(minDaysRemaining);
    }

    public static void RemoveByThumbprint(string thumbprint)
    {
        RemoveFromStore(StoreName.My, thumbprint);
        RemoveFromStore(StoreName.Root, thumbprint);
    }

    private static void RemoveFromStore(StoreName storeName, string thumbprint)
    {
        using var store = new X509Store(storeName, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        foreach (var cert in matches)
        {
            store.Remove(cert);
            cert.Dispose();
        }
    }
}
