using System.Security.Cryptography.X509Certificates;

namespace FirmasPDF.Services;

public sealed record CertificateEntry(X509Certificate2 Certificate, string StoreLabel) : IDisposable
{
    public string Name => Certificate.GetNameInfo(X509NameType.SimpleName, false);
    public string Details => $"{StoreLabel} · Válido hasta {Certificate.NotAfter:dd/MM/yyyy}";
    public string Issuer => Certificate.GetNameInfo(X509NameType.SimpleName, true);
    public void Dispose() => Certificate.Dispose();
}

public static class CertificateService
{
    public static X509Certificate2 LoadPfx(string path, string password)
    {
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password,
            X509KeyStorageFlags.EphemeralKeySet);
        try { EnsureUsable(certificate); return certificate; }
        catch { certificate.Dispose(); throw; }
    }

    public static IReadOnlyList<CertificateEntry> GetWindowsCertificates()
    {
        var result = new List<CertificateEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            foreach (var certificate in store.Certificates)
            {
                if (CanUse(certificate) && seen.Add(certificate.Thumbprint))
                    result.Add(new CertificateEntry(certificate,
                        location == StoreLocation.CurrentUser ? "Usuario actual" : "Equipo"));
                else certificate.Dispose();
            }
        }
        return result.OrderBy(c => c.Name).ToArray();
    }

    public static void EnsureUsable(X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException("El certificado debe incluir su clave privada para poder firmar.");
        if (DateTime.Now < certificate.NotBefore || DateTime.Now > certificate.NotAfter)
            throw new InvalidOperationException("El certificado está caducado o todavía no es válido.");
        var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (usage != null && (usage.KeyUsages & (X509KeyUsageFlags.DigitalSignature |
            X509KeyUsageFlags.NonRepudiation)) == 0)
            throw new InvalidOperationException("El certificado no permite el uso de firma digital.");
    }

    private static bool CanUse(X509Certificate2 certificate)
    {
        try { EnsureUsable(certificate); return true; }
        catch (InvalidOperationException) { return false; }
    }
}
