namespace EssentialCSharp.Web.Services;

public class AuthMessageSenderOptions
{
    public const string AuthMessageSender = "AuthMessageSender";

    /// <summary>Mailjet API key (the basic-auth user name). Required outside Development.</summary>
    public string? APIKey { get; set; }

    /// <summary>Mailjet secret key (the basic-auth password). Required outside Development.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Bare email address used in the From header, e.g. <c>no-reply@example.com</c>. Required outside Development.</summary>
    public string? SendFromEmail { get; set; }

    /// <summary>
    /// Display name for the From header. Optional: Mailjet's Send API v3.1 only requires <c>From.Email</c>.
    /// </summary>
    public string? SendFromName { get; set; }

    /// <summary>When true, Mailjet validates the request but does not deliver the email (Mailjet sandbox mode). Intended for testing the real Mailjet path; leave false in deployed environments.</summary>
    public bool SandboxMode { get; set; }
}
