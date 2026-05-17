using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests;

public class CaptchaValidationServiceTests
{
    [Test]
    public async Task ValidateAsync_MissingConfig_RejectsWithoutVerification()
    {
        StubCaptchaService captchaService = new((_, _, _) => throw new InvalidOperationException("Verifier should not be called."));
        using ServiceProvider serviceProvider = CreateServiceProvider(
            new CaptchaOptions { SecretKey = string.Empty, SiteKey = string.Empty },
            captchaService);

        ICaptchaValidationService validationService = serviceProvider.GetRequiredService<ICaptchaValidationService>();

        CaptchaValidationResult result = await validationService.ValidateAsync("token", "127.0.0.1");

        await Assert.That(result.Outcome).IsEqualTo(CaptchaValidationOutcome.Disabled);
        await Assert.That(result.ShouldProceed).IsFalse();
        await Assert.That(captchaService.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task ValidateAsync_MissingToken_ReturnsMissingToken()
    {
        StubCaptchaService captchaService = new((_, _, _) => throw new InvalidOperationException("Verifier should not be called."));
        using ServiceProvider serviceProvider = CreateServiceProvider(
            new CaptchaOptions { SecretKey = "secret", SiteKey = "sitekey" },
            captchaService);

        ICaptchaValidationService validationService = serviceProvider.GetRequiredService<ICaptchaValidationService>();

        CaptchaValidationResult result = await validationService.ValidateAsync(string.Empty, "127.0.0.1");

        await Assert.That(result.Outcome).IsEqualTo(CaptchaValidationOutcome.MissingToken);
        await Assert.That(result.ShouldProceed).IsFalse();
        await Assert.That(captchaService.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task ValidateAsync_Unavailable_ReturnsUnavailable()
    {
        StubCaptchaService captchaService = new((_, _, _) => Task.FromResult<HCaptchaResult?>(null));
        using ServiceProvider serviceProvider = CreateServiceProvider(
            new CaptchaOptions { SecretKey = "secret", SiteKey = "sitekey" },
            captchaService);

        ICaptchaValidationService validationService = serviceProvider.GetRequiredService<ICaptchaValidationService>();

        CaptchaValidationResult result = await validationService.ValidateAsync("token", "127.0.0.1");

        await Assert.That(result.Outcome).IsEqualTo(CaptchaValidationOutcome.Unavailable);
        await Assert.That(result.ShouldProceed).IsFalse();
        await Assert.That(captchaService.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task ValidateAsync_InvalidAndValid_ReturnExpectedOutcome()
    {
        StubCaptchaService invalidCaptchaService = new((_, _, _) => Task.FromResult<HCaptchaResult?>(new HCaptchaResult
        {
            Success = false,
            ErrorCodes = ["invalid-input-response"]
        }));
        using ServiceProvider invalidProvider = CreateServiceProvider(
            new CaptchaOptions { SecretKey = "secret", SiteKey = "sitekey" },
            invalidCaptchaService);

        ICaptchaValidationService invalidValidationService = invalidProvider.GetRequiredService<ICaptchaValidationService>();
        CaptchaValidationResult invalidResult = await invalidValidationService.ValidateAsync("token", "127.0.0.1");

        await Assert.That(invalidResult.Outcome).IsEqualTo(CaptchaValidationOutcome.Invalid);
        await Assert.That(invalidResult.Response).IsNotNull();
        await Assert.That(invalidResult.ShouldProceed).IsFalse();

        StubCaptchaService validCaptchaService = new((_, _, _) => Task.FromResult<HCaptchaResult?>(new HCaptchaResult
        {
            Success = true
        }));
        using ServiceProvider validProvider = CreateServiceProvider(
            new CaptchaOptions { SecretKey = "secret", SiteKey = "sitekey" },
            validCaptchaService);

        ICaptchaValidationService validValidationService = validProvider.GetRequiredService<ICaptchaValidationService>();
        CaptchaValidationResult validResult = await validValidationService.ValidateAsync("token", "127.0.0.1");

        await Assert.That(validResult.Outcome).IsEqualTo(CaptchaValidationOutcome.Valid);
        await Assert.That(validResult.ShouldProceed).IsTrue();
        await Assert.That(validCaptchaService.CallCount).IsEqualTo(1);
    }

    private static ServiceProvider CreateServiceProvider(CaptchaOptions options, ICaptchaService captchaService)
    {
        ServiceCollection services = new();
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(captchaService);
        services.AddSingleton<ICaptchaValidationService, CaptchaValidationService>();
        return services.BuildServiceProvider();
    }

    private sealed class StubCaptchaService(Func<string?, string?, CancellationToken, Task<HCaptchaResult?>> verifyAsync) : ICaptchaService
    {
        public int CallCount { get; private set; }

        public Task<HCaptchaResult?> VerifyAsync(string secret, string response, string sitekey, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HCaptchaResult?> VerifyAsync(string? response, CancellationToken cancellationToken = default)
            => VerifyAsync(response, remoteIp: null, cancellationToken);

        public async Task<HCaptchaResult?> VerifyAsync(string? response, string? remoteIp, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return await verifyAsync(response, remoteIp, cancellationToken);
        }
    }
}
