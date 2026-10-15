using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace VeyonSync.Core;

public static class CertificateTrust
{
    public static bool Validate(X509Certificate2? certificate, SslPolicyErrors errors, string? pinnedSha256)
    {
        if (certificate == null || DateTime.UtcNow < certificate.NotBefore.ToUniversalTime()
            || DateTime.UtcNow > certificate.NotAfter.ToUniversalTime()) return false;
        if ((errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0) return false;
        if (string.IsNullOrEmpty(pinnedSha256)) return errors == SslPolicyErrors.None;
        if (!string.Equals(certificate.GetCertHashString(HashAlgorithmName.SHA256), pinnedSha256, StringComparison.OrdinalIgnoreCase)) return false;
        // Application-scoped trust anchor: validate the chain, usage and validity against the explicitly selected certificate.
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(certificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // local self-signed anchor has no revocation endpoint
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        return chain.Build(certificate);
    }
    public static HttpClient Client(AgentConfig config)
    {
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, cert, _, errors) => Validate(cert, errors, config.CertificateSha256);
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }
}
