using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests.Integration;

[ClassDataSource<CaptchaServiceProvider>(Shared = SharedType.PerClass)]
public class CaptchaTests(CaptchaServiceProvider serviceProvider)
{
    [Test]
    [Timeout(30_000)]
    public async Task CaptchaService_Verify_Success(CancellationToken cancellationToken)
    {
        ICaptchaService captchaService = serviceProvider.ServiceProvider.GetRequiredService<ICaptchaService>();

        // From https://docs.hcaptcha.com/#integration-testing-test-keys
        string hCaptchaSecret = "0x0000000000000000000000000000000000000000";
        string hCaptchaToken = "10000000-aaaa-bbbb-cccc-000000000001";
        string hCaptchaSiteKey = "10000000-ffff-ffff-ffff-000000000001";
        // Note: cancellationToken is not passed to VerifyAsync because ICaptchaService.VerifyAsync
        // does not currently accept a CancellationToken. If the timeout fires, the underlying
        // HTTP request will continue running in the background.
        HCaptchaResult? response = await captchaService.VerifyAsync(hCaptchaSecret, hCaptchaToken, hCaptchaSiteKey);

        await Assert.That(response).IsNotNull();
        await Assert.That(response.Success).IsTrue();
    }
}

public class CaptchaServiceProvider : IDisposable, IAsyncDisposable
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
    public void Dispose()
    {
        ServiceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await ServiceProvider.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}