using System.Security.Cryptography.X509Certificates;

namespace FirmasPDF.Models;

public sealed class SigningRequest(X509Certificate2 certificate, string reason,
    string location, bool useSelectedAppearance) : IDisposable
{
    public X509Certificate2 Certificate { get; } = certificate;
    public string Reason { get; } = reason;
    public string Location { get; } = location;
    public bool UseSelectedAppearance { get; } = useSelectedAppearance;
    public void Dispose() => Certificate.Dispose();
}
