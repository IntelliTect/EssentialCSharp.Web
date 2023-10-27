using Microsoft.Extensions.DependencyInjection;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using EssentialCSharp.Web.Models;

namespace EssentialCSharp.Web.Extensions.Tests.Integration;

public class CaptchaTests : IClassFixture<WebApplicationFactory<Program>>
{
#pragma warning disable IDE1006 // Naming Styles
    private readonly WebApplicationFactory<Program> _factory;
#pragma warning restore IDE1006 // Naming Styles

    public CaptchaTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CaptchaService_Verify_Success()
    {
        ICaptchaService captchaService = _factory.Services.GetRequiredService<ICaptchaService>();

        // From https://docs.hcaptcha.com/#integration-testing-test-keys
        string hCaptchaSecret = "0x0000000000000000000000000000000000000000";
        string hCaptchaToken = "10000000-aaaa-bbbb-cccc-000000000001";
        string hCaptchaSiteKey = "10000000-ffff-ffff-ffff-000000000001";
        HCaptchaResult? response = await captchaService.Verify(hCaptchaSecret, hCaptchaToken, hCaptchaSiteKey);

        Assert.NotNull(response);
        Assert.True(response.Success);
    }
}
