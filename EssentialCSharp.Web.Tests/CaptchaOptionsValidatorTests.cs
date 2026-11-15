using EssentialCSharp.Web.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests;

public class CaptchaOptionsValidatorTests
{
    private const string TestSiteKey = "10000000-ffff-ffff-ffff-000000000001";
    private const string RealSiteKey = "36d1394e-0de2-4610-a28c-1fc0ae229ebf";
    private const string RealSecretKey = "0xREAL";

    [Test]
    [Arguments("Development", false, true)]
    [Arguments("Staging", false, false)]
    [Arguments("Staging", true, true)]
    [Arguments("Production", false, false)]
    [Arguments("Production", true, false)]
    public async Task Validate_WithTestKeys_AllowedOnlyInDevelopmentOrWhenOptedInOutsideProduction(
        string environmentName, bool allowTestKeys, bool expectedSuccess)
    {
        ValidateOptionsResult result = Validate(environmentName, new CaptchaOptions
        {
            SiteKey = TestSiteKey,
            SecretKey = CaptchaOptionsValidator.TestSecretKey,
            AllowTestKeys = allowTestKeys,
        });

        await Assert.That(result.Succeeded).IsEqualTo(expectedSuccess);
    }

    [Test]
    [Arguments(TestSiteKey, RealSecretKey)]
    [Arguments(RealSiteKey, CaptchaOptionsValidator.TestSecretKey)]
    public async Task Validate_WithEitherTestKeyInProduction_Fails(string siteKey, string secretKey)
    {
        ValidateOptionsResult result = Validate("Production", new CaptchaOptions { SiteKey = siteKey, SecretKey = secretKey });

        await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    [Arguments("Production")]
    [Arguments("Staging")]
    public async Task Validate_WithRealKeys_Succeeds(string environmentName)
    {
        ValidateOptionsResult result = Validate(environmentName, new CaptchaOptions { SiteKey = RealSiteKey, SecretKey = RealSecretKey });

        await Assert.That(result.Succeeded).IsTrue();
    }

    private static ValidateOptionsResult Validate(string environmentName, CaptchaOptions options) =>
        new CaptchaOptionsValidator(new HostingEnvironment { EnvironmentName = environmentName }).Validate(Options.DefaultName, options);
}
