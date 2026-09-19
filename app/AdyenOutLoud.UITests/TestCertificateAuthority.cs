using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AdyenOutLoud.UITests;

/// <summary>
/// A throwaway CA plus a <c>localhost</c>/<c>127.0.0.1</c> server certificate signed by it. The CA is installed
/// as a trusted root in the test simulator only, so the real app can make a genuine <c>wss://</c> connection
/// to a local relay without any certificate-validation bypass in the app.
/// </summary>
internal sealed class TestCertificateAuthority : IDisposable
{
    private readonly X509Certificate2 _authority;

    public TestCertificateAuthority()
    {
        var now = DateTimeOffset.UtcNow;
        using var authorityKey = RSA.Create(2048);
        var authorityRequest = new CertificateRequest("CN=Adyen Out Loud UI test CA", authorityKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        authorityRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        authorityRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        _authority = authorityRequest.CreateSelfSigned(now.AddDays(-1), now.AddDays(30));

        using var serverKey = RSA.Create(2048);
        var serverRequest = new CertificateRequest("CN=localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        serverRequest.CertificateExtensions.Add(names.Build());
        serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var issued = serverRequest.Create(_authority, now.AddDays(-1), now.AddDays(29), RandomNumberGenerator.GetBytes(16));
        using var withKey = issued.CopyWithPrivateKey(serverKey);
        ServerCertificate = X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null);
    }

    public X509Certificate2 ServerCertificate { get; }

    public string AuthorityPem => _authority.ExportCertificatePem();

    public void Dispose()
    {
        ServerCertificate.Dispose();
        _authority.Dispose();
    }
}
