using EssentialCSharp.Web.Areas.Identity.Services.PasswordValidators;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EssentialCSharp.Web.Tests.Integration;

[ClassDataSource<PwnedPasswordServiceProvider>(Shared = SharedType.PerClass)]
public class PwnedPasswordTests(PwnedPasswordServiceProvider serviceProvider)
{
    [Test]
    public async Task KnownBreachedPassword_IsDetected()
    {
        IPasswordValidator<IdentityUser> validator = serviceProvider.ServiceProvider
            .GetRequiredService<IPasswordValidator<IdentityUser>>();
        Mock<IUserStore<IdentityUser>> store = new();
        using UserManager<IdentityUser> manager = new(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        // "password" → SHA-1 5BAA61E4C9B93F3F0682250B6CF8331B7EE68FD8 — always in HIBP (3.8M+ breaches).
        IdentityResult result = await validator.ValidateAsync(
            manager, new IdentityUser("test"), "password");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Errors.Select(e => e.Code)).Contains("PwnedPassword");
    }
}

public class PwnedPasswordServiceProvider : IDisposable, IAsyncDisposable
{
    public ServiceProvider ServiceProvider { get; } = CreateServiceProvider();

    public static ServiceProvider CreateServiceProvider()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("HaveIBeenPwned", c =>
        {
            c.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
            c.DefaultRequestHeaders.UserAgent.ParseAdd("EssentialCSharp.Web/1.0");
            c.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddTransient<IPasswordValidator<IdentityUser>,
            PwnedPasswordValidator<IdentityUser>>();
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            ServiceProvider.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ServiceProvider.DisposeAsync().ConfigureAwait(false);
        Dispose(disposing: false);
        GC.SuppressFinalize(this);
    }
}
