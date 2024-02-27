using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Extensions.Tests.Integration;

public class CaptchaTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact()]
    public async Task CaptchaService_Verify_Success()
    {
        ICaptchaService captchaService = factory.Services.GetRequiredService<ICaptchaService>();

        // From https://docs.hcaptcha.com/#integration-testing-test-keys
        string hCaptchaSecret = "0x0000000000000000000000000000000000000000";
        string hCaptchaToken = "10000000-aaaa-bbbb-cccc-000000000001";
        string hCaptchaSiteKey = "10000000-ffff-ffff-ffff-000000000001";
        HCaptchaResult? response = await captchaService.VerifyAsync(hCaptchaSecret, hCaptchaToken, hCaptchaSiteKey);

        Assert.NotNull(response);
        Assert.True(response.Success);
    }
}
