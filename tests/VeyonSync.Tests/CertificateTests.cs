using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VeyonSync.Core;
using Xunit;

namespace VeyonSync.Tests;

public sealed class CertificateTests
{
    private static X509Certificate2 Create(string name = "teacher", bool expired = false)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName(name); request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), expired ? DateTimeOffset.UtcNow.AddDays(-1) : DateTimeOffset.UtcNow.AddDays(1));
    }
    [Fact]
    public void SelectedSelfSignedCertificateIsTrustedOnlyForMatchingNameAndFingerprint()
    {
        using var certificate = Create(); var pin = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        Assert.True(CertificateTrust.Validate(certificate, SslPolicyErrors.RemoteCertificateChainErrors, pin));
        Assert.False(CertificateTrust.Validate(certificate, SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors, pin));
        Assert.False(CertificateTrust.Validate(certificate, SslPolicyErrors.RemoteCertificateChainErrors, new string('0', 64)));
        using var other = Create("other"); Assert.False(CertificateTrust.Validate(other, SslPolicyErrors.RemoteCertificateChainErrors, pin));
    }
    [Fact]
    public void ExpiredCertificateAndUntrustedUnpinnedCertificateAreRejected()
    {
        using var expired = Create(expired: true); Assert.False(CertificateTrust.Validate(expired, SslPolicyErrors.None, expired.GetCertHashString(HashAlgorithmName.SHA256)));
        using var certificate = Create(); Assert.False(CertificateTrust.Validate(certificate, SslPolicyErrors.RemoteCertificateChainErrors, null));
    }
}
