using EssentialCSharp.Web.Services;
using Mailjet.Client;
using Mailjet.Client.Exceptions;
using Mailjet.Client.TransactionalEmails;
using Mailjet.Client.TransactionalEmails.Response;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EssentialCSharp.Web.Tests;

public class EmailSenderTests
{
    private const string RecipientEmail = "reader@example.com";
    private const string ConfirmationHtml = "Please confirm your account by <a href='https://essentialcsharp.com/Identity/Account/ConfirmEmail?userId=1&amp;code=abc'>clicking here</a>.";

    private sealed class SenderFixture
    {
        public Mock<IMailjetClient> Client { get; } = new();

        public CapturingLogger<EmailSender> Logger { get; } = new();

        public TransactionalEmail? SentEmail { get; private set; }

        public bool? SandboxFlag { get; private set; }

        public int CallCount { get; private set; }

        public EmailSender Sender { get; }

        public SenderFixture(bool sandboxMode = false)
        {
            Sender = new EmailSender(
                Client.Object,
                Options.Create(new AuthMessageSenderOptions { SendFromEmail = "noreply@example.com", SendFromName = "Test", SandboxMode = sandboxMode }),
                Logger);
        }

        public SenderFixture Returns(TransactionalEmailResponse response)
        {
            Client.Setup(c => c.SendTransactionalEmailAsync(It.IsAny<TransactionalEmail>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Callback<TransactionalEmail, bool, bool>((email, sandbox, _) =>
                {
                    CallCount++;
                    SentEmail = email;
                    SandboxFlag = sandbox;
                })
                .ReturnsAsync(response);
            return this;
        }

        public SenderFixture Throws(Exception exception)
        {
            Client.Setup(c => c.SendTransactionalEmailAsync(It.IsAny<TransactionalEmail>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Callback<TransactionalEmail, bool, bool>((email, sandbox, _) =>
                {
                    CallCount++;
                    SentEmail = email;
                    SandboxFlag = sandbox;
                })
                .ThrowsAsync(exception);
            return this;
        }

        public Task SendAsync(string html = "<p>Body</p>") => Sender.SendEmailAsync(RecipientEmail, "Subject", html);
    }

    private static TransactionalEmailResponse Success() => new() { Messages = [new MessageResult { Status = "success" }] };

    private static TransactionalEmailResponse ErrorWithRecipient() => new()
    {
        Messages =
        [
            new MessageResult
            {
                Status = "error",
                To = [new EmailResult { Email = RecipientEmail }],
                Errors =
                [
                    new SendEmailError
                    {
                        ErrorCode = "mj-0013",
                        StatusCode = 400,
                        ErrorMessage = $"\"{RecipientEmail}\" is an invalid email address.\r\nSecond line",
                        ErrorRelatedTo = ["Messages[0].To[0].Email"],
                    }
                ],
            }
        ]
    };

    [Test]
    public async Task Success_LogsInformationOnly()
    {
        SenderFixture fixture = new SenderFixture().Returns(Success());

        await fixture.SendAsync();

        using (Assert.Multiple())
        {
            await Assert.That(fixture.Logger.Entries.Count).IsEqualTo(1);
            await Assert.That(fixture.Logger.Entries[0].Level).IsEqualTo(LogLevel.Information);
            await Assert.That(fixture.CallCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Success_BuildsMessageWithRecipientSenderSubjectAndHtml()
    {
        SenderFixture fixture = new SenderFixture().Returns(Success());

        await fixture.Sender.SendEmailAsync(RecipientEmail, "Confirm your email", ConfirmationHtml);

        TransactionalEmail email = fixture.SentEmail ?? throw new InvalidOperationException("No email was sent.");

        using (Assert.Multiple())
        {
            await Assert.That(email.To.Single().Email).IsEqualTo(RecipientEmail);
            await Assert.That(email.From.Email).IsEqualTo("noreply@example.com");
            await Assert.That(email.From.Name).IsEqualTo("Test");
            await Assert.That(email.Subject).IsEqualTo("Confirm your email");
            await Assert.That(email.HTMLPart).IsEqualTo(ConfirmationHtml);
        }
    }

    [Test]
    public async Task Success_DisablesClickAndOpenTracking()
    {
        SenderFixture fixture = new SenderFixture().Returns(Success());

        await fixture.SendAsync(ConfirmationHtml);

        TransactionalEmail email = fixture.SentEmail ?? throw new InvalidOperationException("No email was sent.");

        using (Assert.Multiple())
        {
            await Assert.That(email.TrackClicks).IsEqualTo(TrackClicks.disabled);
            await Assert.That(email.TrackOpens).IsEqualTo(TrackOpens.disabled);
        }
    }

    [Test]
    public async Task Success_AddsPlainTextPartThatKeepsTheLinkTarget()
    {
        SenderFixture fixture = new SenderFixture().Returns(Success());

        await fixture.SendAsync(ConfirmationHtml);

        TransactionalEmail email = fixture.SentEmail ?? throw new InvalidOperationException("No email was sent.");
        await Assert.That(email.TextPart).IsEqualTo(
            "Please confirm your account by clicking here (https://essentialcsharp.com/Identity/Account/ConfirmEmail?userId=1&code=abc).");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SandboxMode_IsPassedThroughToMailjet(bool sandboxMode)
    {
        SenderFixture fixture = new SenderFixture(sandboxMode).Returns(Success());

        await fixture.SendAsync();

        await Assert.That(fixture.SandboxFlag).IsEqualTo(sandboxMode);
    }

    [Test]
    public async Task SandboxMode_DefaultsToOff()
    {
        await Assert.That(new AuthMessageSenderOptions().SandboxMode).IsFalse();
    }

    [Test]
    public async Task SandboxMode_SuccessResponseWithEmptyMessageIds_DoesNotThrowAndWarnsNotDelivered()
    {
        // Per Mailjet's sandbox docs the response has Status "success" but no real MessageID/MessageUUID.
        SenderFixture fixture = new SenderFixture(sandboxMode: true).Returns(new TransactionalEmailResponse
        {
            Messages = [new MessageResult { Status = "success", To = [new EmailResult { Email = RecipientEmail, MessageID = 0, MessageUUID = "" }] }]
        });

        await fixture.SendAsync();

        using (Assert.Multiple())
        {
            await Assert.That(fixture.Logger.Entries.Single().Level).IsEqualTo(LogLevel.Warning);
            await Assert.That(fixture.Logger.Entries.Single().Message).Contains("sandbox");
            await Assert.That(fixture.Logger.Entries.Single().Message).DoesNotContain("queued successfully");
        }
    }

    [Test]
    public async Task SandboxMode_ValidationError_StillThrows()
    {
        SenderFixture fixture = new SenderFixture(sandboxMode: true).Returns(ErrorWithRecipient());

        await Assert.ThrowsAsync<EmailSendException>(() => fixture.SendAsync());
    }

    [Test]
    public async Task NoMessages_LogsAlertableErrorMessageAndThrows()
    {
        SenderFixture fixture = new SenderFixture().Returns(new TransactionalEmailResponse { Messages = [] });

        EmailSendException? exception = await Assert.ThrowsAsync<EmailSendException>(() => fixture.SendAsync());

        using (Assert.Multiple())
        {
            await Assert.That(fixture.Logger.Entries.Count).IsEqualTo(1);
            await Assert.That(fixture.Logger.Entries[0].Level).IsEqualTo(LogLevel.Error);
            // The Azure Monitor email-failure alert matches this exact text.
            await Assert.That(fixture.Logger.Entries[0].Message).Contains("Unexpectedly no messages returned");
            await Assert.That(exception?.InnerException).IsNull();
        }
    }

    [Test]
    public async Task NullMessages_LogsErrorAndThrows()
    {
        // Models the SDK returning a response whose Messages array is null (the property is not annotated nullable).
        SenderFixture fixture = new SenderFixture().Returns(new TransactionalEmailResponse { Messages = null! });

        await Assert.ThrowsAsync<EmailSendException>(() => fixture.SendAsync());

        using (Assert.Multiple())
        {
            await Assert.That(fixture.Logger.Entries[0].Level).IsEqualTo(LogLevel.Error);
            await Assert.That(fixture.Logger.Entries[0].Message).Contains("Unexpectedly no messages returned");
        }
    }

    [Test]
    public async Task Failure_LogsMailjetErrorReason_WithoutRecipientAddress_AndThrows()
    {
        SenderFixture fixture = new SenderFixture().Returns(ErrorWithRecipient());

        EmailSendException? exception = await Assert.ThrowsAsync<EmailSendException>(() => fixture.SendAsync());

        string message = fixture.Logger.Entries.Single().Message;

        using (Assert.Multiple())
        {
            await Assert.That(fixture.Logger.Entries.Single().Level).IsEqualTo(LogLevel.Error);
            // The Azure Monitor email-failure alert matches this exact text.
            await Assert.That(message).Contains("Failed to send email");
            await Assert.That(message).Contains("error");
            await Assert.That(message).Contains("mj-0013");
            await Assert.That(message).Contains("HTTP 400");
            await Assert.That(message).Contains("is an invalid email address");
            await Assert.That(message).DoesNotContain(RecipientEmail);
            await Assert.That(message).DoesNotContain("\n");
            await Assert.That(exception?.Message).DoesNotContain(RecipientEmail);
        }
    }

    [Test]
    public async Task Failure_WithoutErrorDetails_StillLogsAlertableMessageAndThrows()
    {
        SenderFixture fixture = new SenderFixture().Returns(new TransactionalEmailResponse
        {
            Messages = [new MessageResult { Status = "error" }]
        });

        await Assert.ThrowsAsync<EmailSendException>(() => fixture.SendAsync());

        using (Assert.Multiple())
        {
            await Assert.That(fixture.Logger.Entries.Single().Message).Contains("Failed to send email");
            await Assert.That(fixture.Logger.Entries.Single().Message).Contains("no error details returned");
        }
    }

    [Test]
    public async Task MailjetServerException_IsLoggedWrappedAndThrown()
    {
        MailjetServerException inner = new(new MailjetResponse(
            isSuccessStatusCode: false,
            statusCode: 401,
            content: JObject.Parse($$"""{"ErrorMessage":"API key authentication/authorization failure for {{RecipientEmail}}.\r\nCheck keys."}""")));
        SenderFixture fixture = new SenderFixture().Throws(inner);

        EmailSendException? exception = await Assert.ThrowsAsync<EmailSendException>(() => fixture.SendAsync());

        string logged = fixture.Logger.Entries.Single().Message;

        using (Assert.Multiple())
        {
            await Assert.That(exception?.InnerException).IsSameReferenceAs(inner);
            await Assert.That(exception?.Message).DoesNotContain(RecipientEmail);
            await Assert.That(fixture.Logger.Entries.Single().Level).IsEqualTo(LogLevel.Error);
            await Assert.That(logged).Contains("Failed to send email");
            await Assert.That(logged).Contains("HTTP status: 401");
            await Assert.That(logged).Contains("API key authentication/authorization failure");
            await Assert.That(logged).DoesNotContain(RecipientEmail);
            await Assert.That(logged).DoesNotContain("\n");
        }
    }

    [Test]
    public async Task InvalidJsonResponse_IsLoggedWrappedAndThrown()
    {
        // The SDK parses JSON response bodies unguarded, so a truncated gateway response throws JsonReaderException.
        JsonReaderException inner = new("Unexpected end of content while loading JObject.");
        SenderFixture fixture = new SenderFixture().Throws(inner);

        EmailSendException? exception = await Assert.ThrowsAsync<EmailSendException>(() => fixture.SendAsync());

        using (Assert.Multiple())
        {
            await Assert.That(exception?.InnerException).IsSameReferenceAs(inner);
            await Assert.That(fixture.Logger.Entries.Single().Level).IsEqualTo(LogLevel.Error);
            await Assert.That(fixture.Logger.Entries.Single().Message).Contains("Failed to send email");
        }
    }

    [Test]
    public async Task HtmlWithNoTextContent_OmitsTheTextPartInsteadOfSendingAnEmptyOne()
    {
        SenderFixture fixture = new SenderFixture().Returns(Success());

        await fixture.SendAsync("<p></p>");

        TransactionalEmail email = fixture.SentEmail ?? throw new InvalidOperationException("No email was sent.");
        using (Assert.Multiple())
        {
            await Assert.That(email.HTMLPart).IsEqualTo("<p></p>");
            await Assert.That(email.TextPart).IsNull();
            // Nothing to convert is not a conversion failure, so no warning is expected.
            await Assert.That(fixture.Logger.Entries.Select(e => e.Level)).DoesNotContain(LogLevel.Warning);
        }
    }

    [Test]
    public async Task HtmlTooLargeForTextConversion_SendsHtmlOnlyAndWarns()
    {
        SenderFixture fixture = new SenderFixture().Returns(Success());
        string hugeHtml = "<p>" + new string('x', HtmlToTextConverter.MaxInputLength) + "</p>";

        await fixture.SendAsync(hugeHtml);

        TransactionalEmail email = fixture.SentEmail ?? throw new InvalidOperationException("No email was sent.");
        using (Assert.Multiple())
        {
            await Assert.That(email.HTMLPart).IsEqualTo(hugeHtml);
            await Assert.That(email.TextPart).IsNull();
            await Assert.That(fixture.Logger.Entries.Select(e => e.Level)).Contains(LogLevel.Warning);
            await Assert.That(fixture.Logger.AllText).Contains("plain-text part");
        }
    }

    [Test]
    public async Task HttpRequestException_IsLoggedWrappedAndThrown()
    {
        HttpRequestException inner = new("Name or service not known");
        SenderFixture fixture = new SenderFixture().Throws(inner);

        EmailSendException? exception = await Assert.ThrowsAsync<EmailSendException>(() => fixture.SendAsync());

        using (Assert.Multiple())
        {
            await Assert.That(exception?.InnerException).IsSameReferenceAs(inner);
            await Assert.That(fixture.Logger.Entries.Single().Message).Contains("Failed to send email");
        }
    }

    [Test]
    public async Task HttpTimeout_IsLoggedWrappedAndThrown()
    {
        // With the Mailjet client's resilience handlers removed, HttpClient.Timeout surfaces as a
        // TaskCanceledException whose inner exception is a TimeoutException (what this models).
        TaskCanceledException inner = new("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException());
        SenderFixture fixture = new SenderFixture().Throws(inner);

        EmailSendException? exception = await Assert.ThrowsAsync<EmailSendException>(() => fixture.SendAsync());

        using (Assert.Multiple())
        {
            await Assert.That(exception?.InnerException).IsSameReferenceAs(inner);
            await Assert.That(fixture.Logger.Entries.Single().Level).IsEqualTo(LogLevel.Error);
        }
    }

    [Test]
    public async Task TaskCanceledWithoutTimeoutInner_IsNotWrapped()
    {
        SenderFixture fixture = new SenderFixture().Throws(new TaskCanceledException("canceled"));

        await Assert.ThrowsAsync<TaskCanceledException>(() => fixture.SendAsync());
    }
}
