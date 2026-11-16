using EssentialCSharp.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests;

public class AuthMessageSenderOptionsValidatorTests
{
    private static AuthMessageSenderOptions ValidOptions() => new()
    {
        APIKey = "key",
        SecretKey = "secret",
        SendFromEmail = "no-reply@example.com",
        SendFromName = "Essential C# Team",
    };

    private static ValidateOptionsResult Validate(AuthMessageSenderOptions options) =>
        new AuthMessageSenderOptionsValidator().Validate(Options.DefaultName, options);

    private static async Task<IReadOnlyList<string>> FailuresAsync(ValidateOptionsResult result)
    {
        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).IsNotNull();
        return [.. result.Failures ?? []];
    }

    [Test]
    public async Task Validate_WithAllSettings_Succeeds()
    {
        await Assert.That(Validate(ValidOptions()).Succeeded).IsTrue();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" ")]
    public async Task Validate_WithoutSendFromName_Succeeds(string? sendFromName)
    {
        AuthMessageSenderOptions options = ValidOptions();
        options.SendFromName = sendFromName;

        await Assert.That(Validate(options).Succeeded).IsTrue();
    }

    [Test]
    [Arguments(nameof(AuthMessageSenderOptions.APIKey), null)]
    [Arguments(nameof(AuthMessageSenderOptions.APIKey), "")]
    [Arguments(nameof(AuthMessageSenderOptions.APIKey), "  ")]
    [Arguments(nameof(AuthMessageSenderOptions.SecretKey), null)]
    [Arguments(nameof(AuthMessageSenderOptions.SecretKey), "")]
    [Arguments(nameof(AuthMessageSenderOptions.SecretKey), "  ")]
    [Arguments(nameof(AuthMessageSenderOptions.SendFromEmail), null)]
    [Arguments(nameof(AuthMessageSenderOptions.SendFromEmail), "")]
    [Arguments(nameof(AuthMessageSenderOptions.SendFromEmail), "  ")]
    public async Task Validate_WithMissingRequiredSetting_FailsNamingTheSetting(string property, string? value)
    {
        AuthMessageSenderOptions options = ValidOptions();
        switch (property)
        {
            case nameof(AuthMessageSenderOptions.APIKey):
                options.APIKey = value;
                break;
            case nameof(AuthMessageSenderOptions.SecretKey):
                options.SecretKey = value;
                break;
            default:
                options.SendFromEmail = value;
                break;
        }

        IReadOnlyList<string> failures = await FailuresAsync(Validate(options));

        using (Assert.Multiple())
        {
            await Assert.That(failures).HasSingleItem();
            await Assert.That(failures[0]).Contains($"AuthMessageSender:{property}");
        }
    }

    [Test]
    [Arguments("not-an-email")]
    [Arguments("missing-domain@")]
    [Arguments("@missing-local.com")]
    [Arguments("two@@example.com")]
    [Arguments("Essential C# <no-reply@example.com>")]
    [Arguments(" no-reply@example.com")]
    [Arguments("a@b")]
    [Arguments("a@localhost")]
    [Arguments("a@example.")]
    public async Task Validate_WithInvalidSendFromEmail_Fails(string sendFromEmail)
    {
        AuthMessageSenderOptions options = ValidOptions();
        options.SendFromEmail = sendFromEmail;

        IReadOnlyList<string> failures = await FailuresAsync(Validate(options));

        using (Assert.Multiple())
        {
            await Assert.That(failures).HasSingleItem();
            await Assert.That(failures[0]).Contains("valid email address");
        }
    }

    [Test]
    [Arguments("user@example.com")]
    [Arguments("no-reply@mail.example.co.uk")]
    [Arguments("user+newsletter@example.com")]
    [Arguments("first.last@example.com")]
    [Arguments("user@bücher.example")]
    public async Task Validate_WithValidSendFromEmail_Succeeds(string sendFromEmail)
    {
        AuthMessageSenderOptions options = ValidOptions();
        options.SendFromEmail = sendFromEmail;

        await Assert.That(Validate(options).Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithNothingConfigured_ReportsEveryRequiredSetting()
    {
        IReadOnlyList<string> failures = await FailuresAsync(Validate(new AuthMessageSenderOptions()));

        string all = string.Join('\n', failures);
        using (Assert.Multiple())
        {
            await Assert.That(all).Contains("AuthMessageSender:APIKey");
            await Assert.That(all).Contains("AuthMessageSender:SecretKey");
            await Assert.That(all).Contains("AuthMessageSender:SendFromEmail");
        }
    }

    [Test]
    public async Task SandboxMode_DefaultsToFalse()
    {
        await Assert.That(new AuthMessageSenderOptions().SandboxMode).IsFalse();
    }

    [Test]
    public async Task SandboxMode_BindsFromConfigurationKey()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AuthMessageSender:SandboxMode"] = "true" })
            .Build();

        AuthMessageSenderOptions? options = configuration
            .GetSection(AuthMessageSenderOptions.AuthMessageSender)
            .Get<AuthMessageSenderOptions>();

        await Assert.That(options).IsNotNull();
        await Assert.That(options?.SandboxMode).IsTrue();
    }
}
