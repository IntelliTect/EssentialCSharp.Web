using Microsoft.Extensions.DependencyInjection;
using System.Net;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EssentialCSharp.Web.Extensions.Tests.Integration;

public class CaptchaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CaptchaTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CaptchaService_Verify_Success()
    {
        ICaptchaService? _CaptchaService = _factory.Services.GetService<ICaptchaService>();
        Assert.NotNull(_CaptchaService);
        // From https://docs.hcaptcha.com/#integration-testing-test-keys
        string hCaptchaSecret = "0x0000000000000000000000000000000000000000";
        string hCaptchaToken = "10000000-aaaa-bbbb-cccc-000000000001";
        HttpResponseMessage response = await _CaptchaService.Verify(hCaptchaSecret, hCaptchaToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
