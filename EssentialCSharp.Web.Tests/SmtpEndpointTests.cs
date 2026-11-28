using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Tests;

public class SmtpEndpointTests
{
    [Test]
    [Arguments("Endpoint=smtp://mailpit:1025", "mailpit", 1025)]
    [Arguments("endpoint=SMTP://localhost:2525", "localhost", 2525)]
    public async Task TryParse_SmtpEndpointWithPort_ReturnsHostAndPort(string connectionString, string host, int port)
    {
        bool parsed = SmtpEndpoint.TryParse(connectionString, out SmtpEndpoint? endpoint);

        await Assert.That(parsed).IsTrue();
        await Assert.That(endpoint).IsEqualTo(new SmtpEndpoint(host, port));
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("Endpoint=http://mailpit:1025")]
    [Arguments("Endpoint=smtp://mailpit")]
    [Arguments("Endpoint=smtp://mailpit:99999")]
    [Arguments("Endpoint=smtp://mailpit:notaport")]
    [Arguments("Endpoint=")]
    [Arguments("Host=mailpit;Port=1025")]
    [Arguments("garbage")]
    [Arguments("Endpoint=not a uri")]
    public async Task TryParse_NotAnSmtpEndpointWithPort_ReturnsFalse(string? connectionString)
    {
        bool parsed = SmtpEndpoint.TryParse(connectionString, out SmtpEndpoint? endpoint);

        await Assert.That(parsed).IsFalse();
        await Assert.That(endpoint).IsNull();
    }
}
