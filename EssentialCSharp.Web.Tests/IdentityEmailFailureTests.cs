using System.Security.Claims;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Areas.Identity.Pages.Account;
using EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage;
using EssentialCSharp.Web.Areas.Identity.Services;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using EssentialCSharp.Web.Services.Referrals;
using Mailjet.Client;
using Mailjet.Client.Exceptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Newtonsoft.Json.Linq;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Verifies how the Identity pages react when <see cref="IEmailSender"/> throws <see cref="EmailSendException"/>:
/// pages for a signed-in or just-registered user show a friendly message, while anonymous pages never reveal whether an account exists.
/// </summary>
public class IdentityEmailFailureTests
{
    private const string UserEmail = "reader@example.com";
    private const string CallbackUrl = "https://example.test/Identity/Account/Callback";

    private static readonly EmailSendException SendFailure = new("Mailjet is down.");

    private static Mock<UserManager<EssentialCSharpWebUser>> CreateUserManager()
    {
        Mock<UserManager<EssentialCSharpWebUser>> mock = new(
            Mock.Of<IUserStore<EssentialCSharpWebUser>>(),
            Options.Create(new IdentityOptions { SignIn = { RequireConfirmedAccount = true } }),
            Mock.Of<IPasswordHasher<EssentialCSharpWebUser>>(),
            Array.Empty<IUserValidator<EssentialCSharpWebUser>>(),
            Array.Empty<IPasswordValidator<EssentialCSharpWebUser>>(),
            Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            NullLogger<UserManager<EssentialCSharpWebUser>>.Instance);
        mock.Setup(m => m.GetUserIdAsync(It.IsAny<EssentialCSharpWebUser>())).ReturnsAsync("user-id");
        mock.Setup(m => m.GenerateEmailConfirmationTokenAsync(It.IsAny<EssentialCSharpWebUser>())).ReturnsAsync("token");
        mock.Setup(m => m.GeneratePasswordResetTokenAsync(It.IsAny<EssentialCSharpWebUser>())).ReturnsAsync("token");
        mock.Setup(m => m.GenerateChangeEmailTokenAsync(It.IsAny<EssentialCSharpWebUser>(), It.IsAny<string>())).ReturnsAsync("token");
        mock.Setup(m => m.SetAuthenticationTokenAsync(It.IsAny<EssentialCSharpWebUser>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync(IdentityResult.Success);
        return mock;
    }

    private static Mock<SignInManager<EssentialCSharpWebUser>> CreateSignInManager(UserManager<EssentialCSharpWebUser> userManager)
    {
        Mock<SignInManager<EssentialCSharpWebUser>> mock = new(
            userManager,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<EssentialCSharpWebUser>>(),
            Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<EssentialCSharpWebUser>>.Instance,
            Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<EssentialCSharpWebUser>>());
        mock.Setup(m => m.GetExternalAuthenticationSchemesAsync()).ReturnsAsync([]);
        return mock;
    }

    private static Mock<ICaptchaValidationService> CreatePassingCaptcha()
    {
        Mock<ICaptchaValidationService> captcha = new();
        captcha.Setup(c => c.ValidateAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CaptchaValidationResult.FromResponse(new HCaptchaResult { Success = true }));
        return captcha;
    }

    private static IOptions<CaptchaOptions> CaptchaOptionsAccessor() =>
        Options.Create(new CaptchaOptions { SiteKey = "site", SecretKey = "secret" });

    private static IOptions<SiteSettings> SiteSettingsAccessor() =>
        Options.Create(new SiteSettings { BaseUrl = "https://example.test" });

    private static Mock<IEmailSender> CreateFailingSender()
    {
        Mock<IEmailSender> sender = new();
        sender.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(SendFailure);
        return sender;
    }

    private static T Prepare<T>(T model) where T : PageModel
    {
        DefaultHttpContext httpContext = new();
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            [CaptchaOptions.HttpPostResponseKeyName] = "captcha-token",
        });

        Mock<IUrlHelper> url = new();
        url.SetupGet(u => u.ActionContext).Returns(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
        url.Setup(u => u.RouteUrl(It.IsAny<UrlRouteContext>())).Returns(CallbackUrl);
        url.Setup(u => u.Content(It.IsAny<string>())).Returns("/");

        model.PageContext = new PageContext { HttpContext = httpContext };
        model.Url = url.Object;
        model.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return model;
    }

    private static string? TempDataStatusMessage(PageModel model) =>
        model.TempData[EmailFailureMessages.ResendStatusMessageKey] as string;

    // Logging must never include the recipient address, which Mailjet's raw error body (the inner exception) can echo.

    private static Mock<IEmailSender> CreateSenderFailingWithAddressInInnerException()
    {
        MailjetServerException inner = new(new MailjetResponse(
            isSuccessStatusCode: false,
            statusCode: 400,
            content: JObject.Parse($$"""{"ErrorMessage":"\"{{UserEmail}}\" is an invalid email address."}""")));
        EmailSendException failure = new("The email could not be sent because the request to Mailjet failed.", inner);
        Mock<IEmailSender> sender = new();
        sender.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(failure);
        return sender;
    }

    private static async Task AssertLogsWarningWithoutAddress<T>(CapturingLogger<T> logger)
    {
        using (Assert.Multiple())
        {
            await Assert.That(logger.Entries.Select(e => e.Level)).Contains(LogLevel.Warning);
            await Assert.That(logger.Entries.All(e => e.Exception is null)).IsTrue();
            await Assert.That(logger.AllText).DoesNotContain(UserEmail);
            await Assert.That(logger.AllText).DoesNotContain("invalid email address");
        }
    }

    [Test]
    public async Task Register_WhenEmailFails_LogOutputDoesNotContainTheRecipientAddress()
    {
        Mock<UserManager<EssentialCSharpWebUser>> userManager = CreateUserManager();
        userManager.Setup(m => m.CreateAsync(It.IsAny<EssentialCSharpWebUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        CapturingLogger<RegisterModel> logger = new();
        RegisterModel model = Prepare(CreateRegisterModel(userManager, CreateSenderFailingWithAddressInInnerException().Object, logger));

        await model.OnPostAsync();

        await AssertLogsWarningWithoutAddress(logger);
    }

    [Test]
    public async Task ForgotPassword_WhenEmailFails_LogOutputDoesNotContainTheRecipientAddress()
    {
        Mock<UserManager<EssentialCSharpWebUser>> userManager = CreateUserManager();
        EssentialCSharpWebUser user = new();
        userManager.Setup(m => m.FindByEmailAsync(UserEmail)).ReturnsAsync(user);
        userManager.Setup(m => m.IsEmailConfirmedAsync(user)).ReturnsAsync(true);
        CapturingLogger<ForgotPasswordModel> logger = new();
        ForgotPasswordModel model = Prepare(CreateForgotPasswordModel(userManager, CreateSenderFailingWithAddressInInnerException().Object, logger));

        await model.OnPostAsync();

        await AssertLogsWarningWithoutAddress(logger);
    }

    [Test]
    public async Task ResendEmailConfirmation_WhenEmailFails_LogOutputDoesNotContainTheRecipientAddress()
    {
        Mock<UserManager<EssentialCSharpWebUser>> userManager = CreateUserManager();
        userManager.Setup(m => m.FindByEmailAsync(UserEmail)).ReturnsAsync(new EssentialCSharpWebUser());
        CapturingLogger<ResendEmailConfirmationModel> logger = new();
        ResendEmailConfirmationModel model = Prepare(CreateResendModel(userManager, CreateSenderFailingWithAddressInInnerException().Object, logger));

        await model.OnPostAsync();

        await AssertLogsWarningWithoutAddress(logger);
    }

    [Test]
    public async Task ManageEmail_WhenEmailFails_LogOutputDoesNotContainTheRecipientAddress()
    {
        CapturingLogger<EmailModel> changeLogger = new();
        EmailModel change = Prepare(CreateManageEmailModel(CreateSenderFailingWithAddressInInnerException().Object, changeLogger));
        change.Input = new EmailModel.InputModel { NewEmail = UserEmail };
        CapturingLogger<EmailModel> verifyLogger = new();
        EmailModel verify = Prepare(CreateManageEmailModel(CreateSenderFailingWithAddressInInnerException().Object, verifyLogger));
        verify.Input = new EmailModel.InputModel { NewEmail = UserEmail };

        await change.OnPostChangeEmailAsync();
        await verify.OnPostSendVerificationEmailAsync();

        await AssertLogsWarningWithoutAddress(changeLogger);
        await AssertLogsWarningWithoutAddress(verifyLogger);
    }

    [Test]
    public async Task ExternalLogin_WhenEmailFails_LogOutputDoesNotContainTheRecipientAddress()
    {
        Mock<UserManager<EssentialCSharpWebUser>> newUserManager = CreateUserManager();
        newUserManager.Setup(m => m.FindByEmailAsync(UserEmail)).ReturnsAsync((EssentialCSharpWebUser?)null);
        newUserManager.Setup(m => m.CreateAsync(It.IsAny<EssentialCSharpWebUser>())).ReturnsAsync(IdentityResult.Success);
        newUserManager.Setup(m => m.AddLoginAsync(It.IsAny<EssentialCSharpWebUser>(), It.IsAny<UserLoginInfo>())).ReturnsAsync(IdentityResult.Success);
        CapturingLogger<ExternalLoginModel> newUserLogger = new();
        ExternalLoginModel newUser = Prepare(CreateExternalLoginModel(newUserManager, CreateSenderFailingWithAddressInInnerException().Object, newUserLogger));

        Mock<UserManager<EssentialCSharpWebUser>> existingUserManager = CreateUserManager();
        existingUserManager.Setup(m => m.FindByEmailAsync(UserEmail)).ReturnsAsync(new EssentialCSharpWebUser { EmailConfirmed = false });
        CapturingLogger<ExternalLoginModel> existingUserLogger = new();
        ExternalLoginModel existingUser = Prepare(CreateExternalLoginModel(existingUserManager, CreateSenderFailingWithAddressInInnerException().Object, existingUserLogger));

        await newUser.OnPostConfirmationAsync();
        await existingUser.OnPostConfirmationAsync();

        await AssertLogsWarningWithoutAddress(newUserLogger);
        await AssertLogsWarningWithoutAddress(existingUserLogger);
    }

    // Register

    [Test]
    public async Task Register_WhenConfirmationEmailFails_RedirectsToResendWithFriendlyMessage()
    {
        Mock<UserManager<EssentialCSharpWebUser>> userManager = CreateUserManager();
        userManager.Setup(m => m.CreateAsync(It.IsAny<EssentialCSharpWebUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        RegisterModel model = Prepare(CreateRegisterModel(userManager, CreateFailingSender().Object));

        IActionResult result = await model.OnPostAsync();

        RedirectToPageResult redirect = await AssertIsRedirectToPage(result, "ResendEmailConfirmation");
        await Assert.That(redirect.RouteValues?["email"]).IsEqualTo(UserEmail);
        await Assert.That(TempDataStatusMessage(model)).IsEqualTo(EmailFailureMessages.AccountCreatedConfirmationNotSent);
        userManager.Verify(m => m.CreateAsync(It.IsAny<EssentialCSharpWebUser>(), It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task Register_WhenConfirmationEmailSucceeds_RedirectsToRegisterConfirmation()
    {
        Mock<UserManager<EssentialCSharpWebUser>> userManager = CreateUserManager();
        userManager.Setup(m => m.CreateAsync(It.IsAny<EssentialCSharpWebUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        RegisterModel model = Prepare(CreateRegisterModel(userManager, Mock.Of<IEmailSender>()));

        IActionResult result = await model.OnPostAsync();

        await AssertIsRedirectToPage(result, "RegisterConfirmation");
        await Assert.That(TempDataStatusMessage(model)).IsNull();
    }

    [Test]
    public async Task Register_WhenSenderThrowsUnexpectedException_DoesNotHideIt()
    {
        Mock<UserManager<EssentialCSharpWebUser>> userManager = CreateUserManager();
        userManager.Setup(m => m.CreateAsync(It.IsAny<EssentialCSharpWebUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        Mock<IEmailSender> sender = new();
        sender.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("bug"));
        RegisterModel model = Prepare(CreateRegisterModel(userManager, sender.Object));

        await Assert.ThrowsAsync<InvalidOperationException>(() => model.OnPostAsync());
    }

    private static RegisterModel CreateRegisterModel(Mock<UserManager<EssentialCSharpWebUser>> userManager, IEmailSender sender, ILogger<RegisterModel>? logger = null)
    {
        RegisterModel model = new(
            userManager.Object,
            Mock.Of<IUserStore<EssentialCSharpWebUser>>(),
            CreateSignInManager(userManager.Object).Object,
            logger ?? NullLogger<RegisterModel>.Instance,
            sender,
            CreatePassingCaptcha().Object,
            CaptchaOptionsAccessor(),
            Mock.Of<IUserEmailStore<EssentialCSharpWebUser>>(),
            SiteSettingsAccessor(),
            new RegistrationBrowserBinding(new EphemeralDataProtectionProvider(), Options.Create(new DataProtectionTokenProviderOptions())));
        model.Input = new RegisterModel.InputModel
        {
            UserName = "reader",
            FirstName = "Reader",
            LastName = "Example",
            Email = UserEmail,
            Password = "A-long-valid-passphrase-1",
            ConfirmPassword = "A-long-valid-passphrase-1",
        };
        return model;
    }

    // ForgotPassword (must not reveal whether the account exists)

    [Test]
    public async Task ForgotPassword_WhenEmailFails_StillRedirectsToConfirmationPage()
    {
        Mock<UserManager<EssentialCSharpWebUser>> userManager = CreateUserManager();
        EssentialCSharpWebUser user = new();
        userManager.Setup(m => m.FindByEmailAsync(UserEmail)).ReturnsAsync(user);
        userManager.Setup(m => m.IsEmailConfirmedAsync(user)).ReturnsAsync(true);
        Mock<IEmailSender> sender = CreateFailingSender();
        ForgotPasswordModel model = Prepare(CreateForgotPasswordModel(userManager, sender.Object));

        IActionResult result = await model.OnPostAsync();

        await AssertIsRedirectToPage(result, "./ForgotPasswordConfirmation");
        sender.Verify(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        await Assert.That(model.ModelState.ErrorCount).IsEqualTo(0);
    }

    [Test]
    public async Task ForgotPassword_UnknownAccount_RedirectsToTheSameConfirmationPageWithoutSending()
    {
        Mock<UserManager<EssentialCSharpWebUser>> userManager = CreateUserManager();
        userManager.Setup(m => m.FindByEmailAsync(UserEmail)).ReturnsAsync((EssentialCSharpWebUser?)null);
        Mock<IEmailSender> sender = CreateFailingSender();
        ForgotPasswordModel model = Prepare(CreateForgotPasswordModel(userManager, sender.Object));

        IActionResult result = await model.OnPostAsync();

        await AssertIsRedirectToPage(result, "./ForgotPasswordConfirmation");
        sender.Verify(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ForgotPassword_WhenSenderThrowsUnexpectedException_DoesNotHideIt()
    {
        Mock<UserManager<EssentialCSharpWebUser>> userManager = CreateUserManager();
        EssentialCSharpWebUser user = new();
        userManager.Setup(m => m.FindByEmailAsync(UserEmail)).ReturnsAsync(user);
        userManager.Setup(m => m.IsEmailConfirmedAsync(user)).ReturnsAsync(true);
        Mock<IEmailSender> sender = new();
        sender.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("bug"));
        ForgotPasswordModel model = Prepare(CreateForgotPasswordModel(userManager, sender.Object));

        await Assert.ThrowsAsync<InvalidOperationException>(() => model.OnPostAsync());
    }

    private static ForgotPasswordModel CreateForgotPasswordModel(Mock<UserManager<EssentialCSharpWebUser>> userManager, IEmailSender sender, ILogger<ForgotPasswordModel>? logger = null)
    {
        ForgotPasswordModel model = new(
            userManager.Object,
            sender,
            CreatePassingCaptcha().Object,
            CaptchaOptionsAccessor(),
            logger ?? NullLogger<ForgotPasswordModel>.Instance,
            SiteSettingsAccessor());
        model.Input = new ForgotPasswordModel.InputModel { Email = UserEmail };
        return model;
    }

    // ResendEmailConfirmation (anonymous; same neutral response whatever happens)

    [Test]
    public async Task ResendEmailConfirmation_WhenEmailFails_ShowsTheSameNeutralMessageAsAnUnknownAccount()
    {
        Mock<UserManager<EssentialCSharpWebUser>> existingUserManager = CreateUserManager();
        existingUserManager.Setup(m => m.FindByEmailAsync(UserEmail)).ReturnsAsync(new EssentialCSharpWebUser());
        ResendEmailConfirmationModel failing = Prepare(CreateResendModel(existingUserManager, CreateFailingSender().Object));

        Mock<UserManager<EssentialCSharpWebUser>> unknownUserManager = CreateUserManager();
        unknownUserManager.Setup(m => m.FindByEmailAsync(UserEmail)).ReturnsAsync((EssentialCSharpWebUser?)null);
        ResendEmailConfirmationModel unknown = Prepare(CreateResendModel(unknownUserManager, Mock.Of<IEmailSender>()));

        IActionResult failingResult = await failing.OnPostAsync();
        IActionResult unknownResult = await unknown.OnPostAsync();

        using (Assert.Multiple())
        {
            await Assert.That(failingResult).IsTypeOf<PageResult>();
            await Assert.That(unknownResult).IsTypeOf<PageResult>();
            await Assert.That(ModelErrors(failing)).IsEquivalentTo([EmailFailureMessages.ResendNeutral]);
            await Assert.That(ModelErrors(failing)).IsEquivalentTo(ModelErrors(unknown));
        }
    }

    [Test]
    public async Task ResendEmailConfirmation_WhenEmailSucceeds_ShowsTheNeutralMessage()
    {
        Mock<UserManager<EssentialCSharpWebUser>> userManager = CreateUserManager();
        userManager.Setup(m => m.FindByEmailAsync(UserEmail)).ReturnsAsync(new EssentialCSharpWebUser());
        Mock<IEmailSender> sender = new();
        ResendEmailConfirmationModel model = Prepare(CreateResendModel(userManager, sender.Object));

        await model.OnPostAsync();

        sender.Verify(s => s.SendEmailAsync(UserEmail, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        await Assert.That(ModelErrors(model)).IsEquivalentTo([EmailFailureMessages.ResendNeutral]);
    }

    [Test]
    public async Task ResendEmailConfirmation_OnGet_PrefillsEmailFromQuery()
    {
        ResendEmailConfirmationModel model = Prepare(CreateResendModel(CreateUserManager(), Mock.Of<IEmailSender>()));

        model.OnGet(UserEmail);

        await Assert.That(model.Input.Email).IsEqualTo(UserEmail);
    }

    private static string[] ModelErrors(PageModel model) =>
        [.. model.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)];

    private static ResendEmailConfirmationModel CreateResendModel(Mock<UserManager<EssentialCSharpWebUser>> userManager, IEmailSender sender, ILogger<ResendEmailConfirmationModel>? logger = null)
    {
        ResendEmailConfirmationModel model = new(
            userManager.Object,
            sender,
            CreatePassingCaptcha().Object,
            CaptchaOptionsAccessor(),
            logger ?? NullLogger<ResendEmailConfirmationModel>.Instance,
            SiteSettingsAccessor());
        model.Input = new ResendEmailConfirmationModel.InputModel { Email = UserEmail };
        return model;
    }

    // Manage > Email (signed in, so a visible error does not reveal anything)

    [Test]
    public async Task ManageEmail_ChangeEmail_WhenEmailFails_ShowsFriendlyErrorStatus()
    {
        EmailModel model = Prepare(CreateManageEmailModel(CreateFailingSender().Object));
        model.Input = new EmailModel.InputModel { NewEmail = "new@example.com" };

        IActionResult result = await model.OnPostChangeEmailAsync();

        using (Assert.Multiple())
        {
            await Assert.That(result).IsTypeOf<RedirectToPageResult>();
            await Assert.That(model.StatusMessage).IsEqualTo(EmailFailureMessages.TryAgainLater);
            await Assert.That(model.StatusMessage).StartsWith("Error");
        }
    }

    [Test]
    public async Task ManageEmail_SendVerificationEmail_WhenEmailFails_ShowsFriendlyErrorStatus()
    {
        EmailModel model = Prepare(CreateManageEmailModel(CreateFailingSender().Object));
        model.Input = new EmailModel.InputModel { NewEmail = "new@example.com" };

        IActionResult result = await model.OnPostSendVerificationEmailAsync();

        using (Assert.Multiple())
        {
            await Assert.That(result).IsTypeOf<RedirectToPageResult>();
            await Assert.That(model.StatusMessage).IsEqualTo(EmailFailureMessages.TryAgainLater);
        }
    }

    [Test]
    public async Task ManageEmail_ChangeEmail_WhenEmailSucceeds_ReportsSuccess()
    {
        EmailModel model = Prepare(CreateManageEmailModel(Mock.Of<IEmailSender>()));
        model.Input = new EmailModel.InputModel { NewEmail = "new@example.com" };

        await model.OnPostChangeEmailAsync();

        await Assert.That(model.StatusMessage).Contains("Confirmation link to change email sent");
    }

    private static EmailModel CreateManageEmailModel(IEmailSender sender, ILogger<EmailModel>? logger = null)
    {
        Mock<UserManager<EssentialCSharpWebUser>> userManager = CreateUserManager();
        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(new EssentialCSharpWebUser());
        userManager.Setup(m => m.GetEmailAsync(It.IsAny<EssentialCSharpWebUser>())).ReturnsAsync("old@example.com");
        return new EmailModel(userManager.Object, CreateSignInManager(userManager.Object).Object, sender, logger ?? NullLogger<EmailModel>.Instance, SiteSettingsAccessor());
    }

    // ExternalLogin confirmation

    [Test]
    public async Task ExternalLogin_NewUser_WhenConfirmationEmailFails_RedirectsToResendWithFriendlyMessage()
    {
        Mock<UserManager<EssentialCSharpWebUser>> userManager = CreateUserManager();
        userManager.Setup(m => m.FindByEmailAsync(UserEmail)).ReturnsAsync((EssentialCSharpWebUser?)null);
        userManager.Setup(m => m.CreateAsync(It.IsAny<EssentialCSharpWebUser>())).ReturnsAsync(IdentityResult.Success);
        userManager.Setup(m => m.AddLoginAsync(It.IsAny<EssentialCSharpWebUser>(), It.IsAny<UserLoginInfo>())).ReturnsAsync(IdentityResult.Success);
        ExternalLoginModel model = Prepare(CreateExternalLoginModel(userManager, CreateFailingSender().Object));

        IActionResult result = await model.OnPostConfirmationAsync();

        RedirectToPageResult redirect = await AssertIsRedirectToPage(result, "./ResendEmailConfirmation");

        using (Assert.Multiple())
        {
            await Assert.That(redirect.RouteValues?["email"]).IsEqualTo(UserEmail);
            await Assert.That(TempDataStatusMessage(model)).IsEqualTo(EmailFailureMessages.AccountCreatedConfirmationNotSent);
        }
        // The typed email is unverified, so the external login must not be linked yet.
        userManager.Verify(m => m.AddLoginAsync(It.IsAny<EssentialCSharpWebUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    [Test]
    public async Task ExternalLogin_ExistingUnconfirmedUser_WhenEmailFails_ShowsTheSameMessageAsSuccess()
    {
        Mock<UserManager<EssentialCSharpWebUser>> failingManager = CreateUserManager();
        failingManager.Setup(m => m.FindByEmailAsync(UserEmail)).ReturnsAsync(new EssentialCSharpWebUser { EmailConfirmed = false });
        ExternalLoginModel failing = Prepare(CreateExternalLoginModel(failingManager, CreateFailingSender().Object));

        Mock<UserManager<EssentialCSharpWebUser>> succeedingManager = CreateUserManager();
        succeedingManager.Setup(m => m.FindByEmailAsync(UserEmail)).ReturnsAsync(new EssentialCSharpWebUser { EmailConfirmed = false });
        ExternalLoginModel succeeding = Prepare(CreateExternalLoginModel(succeedingManager, Mock.Of<IEmailSender>()));

        IActionResult failingResult = await failing.OnPostConfirmationAsync();
        IActionResult succeedingResult = await succeeding.OnPostConfirmationAsync();

        using (Assert.Multiple())
        {
            await Assert.That(failingResult).IsTypeOf<PageResult>();
            await Assert.That(succeedingResult).IsTypeOf<PageResult>();
            await Assert.That(ModelErrors(failing)).IsEquivalentTo(ModelErrors(succeeding));
            await Assert.That(TempDataStatusMessage(failing)).IsNull();
        }
    }

    private static ExternalLoginModel CreateExternalLoginModel(Mock<UserManager<EssentialCSharpWebUser>> userManager, IEmailSender sender, ILogger<ExternalLoginModel>? logger = null)
    {
        Mock<SignInManager<EssentialCSharpWebUser>> signInManager = CreateSignInManager(userManager.Object);
        signInManager.Setup(m => m.GetExternalLoginInfoAsync(It.IsAny<string?>())).ReturnsAsync(
            new ExternalLoginInfo(new ClaimsPrincipal(new ClaimsIdentity()), "GitHub", "provider-key", "GitHub"));

        ExternalLoginModel model = new(
            signInManager.Object,
            userManager.Object,
            Mock.Of<IUserStore<EssentialCSharpWebUser>>(),
            logger ?? NullLogger<ExternalLoginModel>.Instance,
            sender,
            Mock.Of<IUserEmailStore<EssentialCSharpWebUser>>(),
            Mock.Of<IReferralService>(),
            CreatePassingCaptcha().Object,
            CaptchaOptionsAccessor(),
            SiteSettingsAccessor(),
            // The token service is not reached by the confirmation handler.
            new AccountOwnershipService(userManager.Object, new McpApiTokenService(null!)));
        model.Input = new ExternalLoginModel.InputModel { Email = UserEmail };
        return model;
    }

    private static async Task<RedirectToPageResult> AssertIsRedirectToPage(IActionResult result, string pageName)
    {
        await Assert.That(result).IsTypeOf<RedirectToPageResult>();
        RedirectToPageResult redirect = (RedirectToPageResult)result;
        await Assert.That(redirect.PageName).IsEqualTo(pageName);
        return redirect;
    }
}
