using System.Data.Common;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EssentialCSharp.Web.Services;

/// <summary>The plain-SMTP server that <see cref="SmtpEmailSender"/> delivers to.</summary>
/// <param name="Host">The SMTP host name.</param>
/// <param name="Port">The SMTP port.</param>
public sealed record SmtpEndpoint(string Host, int Port)
{
    /// <summary>The connection string name that Aspire's MailPit resource exposes to referencing projects.</summary>
    public const string ConnectionStringName = "mailpit";

    /// <summary>Parses an Aspire connection string such as <c>Endpoint=smtp://localhost:1025</c>.</summary>
    /// <returns><see langword="true"/> when the connection string names an <c>smtp://</c> endpoint.</returns>
    public static bool TryParse(string? connectionString, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SmtpEndpoint? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        DbConnectionStringBuilder builder = new();
        try
        {
            builder.ConnectionString = connectionString;
        }
        catch (ArgumentException)
        {
            // Not key=value syntax at all.
            return false;
        }

        if (!builder.TryGetValue("Endpoint", out object? value)
            || !Uri.TryCreate(value?.ToString(), UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, "smtp", StringComparison.OrdinalIgnoreCase)
            || uri.Port <= 0)
        {
            return false;
        }

        endpoint = new SmtpEndpoint(uri.Host, uri.Port);
        return true;
    }
}

/// <summary>
/// Sends Identity email over unencrypted SMTP with MailKit. Intended for a local mail catcher such as MailPit,
/// which accepts plain SMTP without TLS or authentication by default.
/// </summary>
public sealed partial class SmtpEmailSender(
    SmtpEndpoint endpoint,
    IOptions<AuthMessageSenderOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private const string DefaultFromEmail = "no-reply@essentialcsharp.localhost";
    private const string DefaultFromName = "Essential C#";

    private readonly ILogger _Logger = logger;

    private AuthMessageSenderOptions Options { get; } = options.Value;

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        using MimeMessage message = new();
        message.From.Add(new MailboxAddress(
            string.IsNullOrWhiteSpace(Options.SendFromName) ? DefaultFromName : Options.SendFromName,
            string.IsNullOrWhiteSpace(Options.SendFromEmail) ? DefaultFromEmail : Options.SendFromEmail));
        message.To.Add(new MailboxAddress(string.Empty, email));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlMessage }.ToMessageBody();

        // Follows MailKit's README send sequence: connect, send, DisconnectAsync(true), one client per message.
        // No AuthenticateAsync: MailPit accepts unauthenticated mail. None, not StartTlsWhenAvailable, because
        // MailPit offers no STARTTLS by default and this states that the connection is plaintext.
        using SmtpClient client = new();
        await client.ConnectAsync(endpoint.Host, endpoint.Port, SecureSocketOptions.None);
        await client.SendAsync(message);
        await client.DisconnectAsync(quit: true);
        LogEmailSent(_Logger, endpoint.Host, endpoint.Port);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email sent to SMTP server {Host}:{Port}.")]
    private static partial void LogEmailSent(ILogger logger, string host, int port);
}
