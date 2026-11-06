using System.Net;
using EssentialCSharp.Web.Areas.Identity.Services.PasswordValidators;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EssentialCSharp.Web.Tests;

public class PwnedPasswordValidatorTests
{
    // SHA-1("password") = 5BAA61E4C9B93F3F0682250B6CF8331B7EE68FD8
    // Prefix: 5BAA6, Suffix: 1E4C9B93F3F0682250B6CF8331B7EE68FD8

    [Test]
    public async Task BreachedPassword_ReturnsFailedResult()
    {
        // The response contains the real suffix for "password" with a high count.
        string responseBody =
            "1D2DA4D3D1F8B4BAA725C0A2E3B4D5E6F7A:2\r\n" +
            "1E4C9B93F3F0682250B6CF8331B7EE68FD8:3861493\r\n" +
            "1F2E3D4C5B6A79808172635445362718091:15\r\n";

        PwnedPasswordValidator<IdentityUser> validator = CreateValidator(responseBody);
        using UserManager<IdentityUser> manager = CreateMockUserManager();

        IdentityResult result = await validator.ValidateAsync(manager, new IdentityUser("testuser"), "password");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Errors.Select(e => e.Code)).Contains("PwnedPassword");
    }

    [Test]
    public async Task SafePassword_ReturnsSuccess()
    {
        // Response contains suffixes that do NOT match the test password's hash.
        string responseBody =
            "0000000000000000000000000000000000A:5\r\n" +
            "1111111111111111111111111111111111B:12\r\n" +
            "2222222222222222222222222222222222C:1\r\n";

        PwnedPasswordValidator<IdentityUser> validator = CreateValidator(responseBody);
        using UserManager<IdentityUser> manager = CreateMockUserManager();

        IdentityResult result = await validator.ValidateAsync(manager, new IdentityUser("testuser"), "s0m3-V3ry-Un1qu3-P@ssw0rd!");

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ApiError_FailsOpen_ReturnsSuccess()
    {
        PwnedPasswordValidator<IdentityUser> validator = CreateValidator(
            throwOnSend: new HttpRequestException("Simulated HIBP outage"));
        using UserManager<IdentityUser> manager = CreateMockUserManager();

        IdentityResult result = await validator.ValidateAsync(manager, new IdentityUser("testuser"), "anything");

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task PaddedEntry_WithCountZero_IsIgnored()
    {
        // Simulate a padded response where the matching suffix has count=0.
        string responseBody =
            "1D2DA4D3D1F8B4BAA725C0A2E3B4D5E6F7A:2\r\n" +
            "1E4C9B93F3F0682250B6CF8331B7EE68FD8:0\r\n" +
            "1F2E3D4C5B6A79808172635445362718091:15\r\n";

        PwnedPasswordValidator<IdentityUser> validator = CreateValidator(responseBody);
        using UserManager<IdentityUser> manager = CreateMockUserManager();

        IdentityResult result = await validator.ValidateAsync(manager, new IdentityUser("testuser"), "password");

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ServiceUnavailableResponse_FailsOpen_ReturnsSuccess()
    {
        PwnedPasswordValidator<IdentityUser> validator =
            CreateValidator("", HttpStatusCode.ServiceUnavailable);
        using UserManager<IdentityUser> manager = CreateMockUserManager();

        IdentityResult result = await validator.ValidateAsync(manager, new IdentityUser("testuser"), "anything");

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_SendsCorrectKAnonymityRequest()
    {
        // SHA-1("password") = 5BAA61E4C9B93F3F0682250B6CF8331B7EE68FD8
        // Validator must send only the 5-char prefix "5BAA6", never the full hash.
        HttpRequestMessage? capturedRequest = null;
        MockHttpMessageHandler handler = new((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty)
            });
        });
        PwnedPasswordValidator<IdentityUser> validator = BuildValidator(handler);
        using UserManager<IdentityUser> manager = CreateMockUserManager();

        await validator.ValidateAsync(manager, new IdentityUser("testuser"), "password");

        await Assert.That(capturedRequest).IsNotNull();
        await Assert.That(capturedRequest!.RequestUri!.PathAndQuery).IsEqualTo("/range/5BAA6");
        await Assert.That(capturedRequest.Headers.Contains("Add-Padding")).IsTrue();
        await Assert.That(capturedRequest.Headers.GetValues("Add-Padding").Single()).IsEqualTo("true");
    }


    [Test]
    public async Task NullPassword_ThrowsArgumentNullException()
    {
        PwnedPasswordValidator<IdentityUser> validator = CreateValidator("UNUSED:0\r\n");
        using UserManager<IdentityUser> manager = CreateMockUserManager();

        await Assert.That(async () => await validator.ValidateAsync(manager, new IdentityUser("testuser"), null!))
            .ThrowsExactly<ArgumentNullException>()
            .And
            .HasMessageContaining("password");
    }

    [Test]
    public async Task NullManager_ThrowsArgumentNullException()
    {
        PwnedPasswordValidator<IdentityUser> validator = CreateValidator("UNUSED:0\r\n");

        await Assert.That(async () => await validator.ValidateAsync(null!, new IdentityUser("testuser"), "test"))
            .ThrowsExactly<ArgumentNullException>()
            .And
            .HasMessageContaining("manager");
    }

    private static PwnedPasswordValidator<IdentityUser> CreateValidator(
        string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        MockHttpMessageHandler handler = new((_, _) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody)
            }));

        return BuildValidator(handler);
    }

    private static PwnedPasswordValidator<IdentityUser> CreateValidator(
        HttpRequestException throwOnSend)
    {
        MockHttpMessageHandler handler = new((_, _) =>
            throw throwOnSend);

        return BuildValidator(handler);
    }

    private static PwnedPasswordValidator<IdentityUser> BuildValidator(MockHttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.pwnedpasswords.com/")
        };

        Mock<IHttpClientFactory> factory = new();
        factory.Setup(f => f.CreateClient("HaveIBeenPwned")).Returns(httpClient);

        return new PwnedPasswordValidator<IdentityUser>(
            factory.Object,
            NullLogger<PwnedPasswordValidator<IdentityUser>>.Instance);
    }

    private static UserManager<IdentityUser> CreateMockUserManager()
    {
        Mock<IUserStore<IdentityUser>> store = new();
        return new UserManager<IdentityUser>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private sealed class MockHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
