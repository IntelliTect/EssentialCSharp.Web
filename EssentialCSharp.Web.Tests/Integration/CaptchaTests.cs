using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Extensions.Tests.Integration;

public class CaptchaTests(CaptchaServiceProvider serviceProvider) : IClassFixture<CaptchaServiceProvider>
{
    [Fact]
    public async Task CaptchaService_Verify_Success()
    {
        ICaptchaService captchaService = serviceProvider.ServiceProvider.GetRequiredService<ICaptchaService>();

        // From https://docs.hcaptcha.com/#integration-testing-test-keys
        string hCaptchaSecret = "0x0000000000000000000000000000000000000000";
        string hCaptchaToken = "10000000-aaaa-bbbb-cccc-000000000001";
        string hCaptchaSiteKey = "10000000-ffff-ffff-ffff-000000000001";
        HCaptchaResult? response = await captchaService.VerifyAsync(hCaptchaSecret, hCaptchaToken, hCaptchaSiteKey);

        Assert.NotNull(response);
        Assert.True(response.Success);
    }
}

public class CaptchaServiceProvider
{
    public ServiceProvider ServiceProvider { get; } = CreateServiceProvider();
    public static ServiceProvider CreateServiceProvider()
    {
        IServiceCollection services = new ServiceCollection();

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(IntelliTect.Multitool.RepositoryPaths.GetDefaultRepoRoot())
            .AddJsonFile($"{nameof(EssentialCSharp)}.{nameof(Web)}/appsettings.json")
            .Build();
        services.AddCaptchaService(configuration.GetSection(CaptchaOptions.CaptchaSender));
        // Add other necessary services here

        return services.BuildServiceProvider();
    }
}
