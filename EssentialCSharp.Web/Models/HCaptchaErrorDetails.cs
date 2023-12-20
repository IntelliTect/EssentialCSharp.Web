using System.Diagnostics.CodeAnalysis;

namespace EssentialCSharp.Web.Models;

public record class HCaptchaErrorDetails
{
    public const string MissingInputSecret = "missing-input-secret";
    public const string InvalidInputSecret = "invalid-input-secret";
    public const string MissingInputResponse = "missing-input-response";
    public const string InvalidInputResponse = "invalid-input-response";
    public const string BadRequest = "bad-request";
    public const string InvalidOrAlreadySeenResponse = "invalid-or-already-seen-response";
    public const string NotUsingDummyPasscode = "not-using-dummy-passcode";
    public const string SitekeySecretMismatch = "sitekey-secret-mismatch";

    private static readonly IReadOnlyDictionary<string, HCaptchaErrorDetails> _ErrorCodeDescriptionDictionary = new Dictionary<string, HCaptchaErrorDetails>()
    {
        { MissingInputSecret, new(MissingInputSecret, "Your secret key is missing.", null) },
        { InvalidInputSecret, new(InvalidInputSecret, "Your secret key is invalid or malformed.", null) },
        { MissingInputResponse, new(MissingInputResponse, "The response parameter (verification token) is missing.", "Please fill complete the captcha and try again.") },
        { InvalidInputResponse, new(InvalidInputResponse, "The response parameter (verification token) is invalid or malformed.", null) },
        { BadRequest, new(BadRequest, "The request is invalid or malformed.", null) },
        { InvalidOrAlreadySeenResponse, new(InvalidOrAlreadySeenResponse, "The response parameter has already been checked, or has another issue.", null) },
        { NotUsingDummyPasscode, new(NotUsingDummyPasscode, "You have used a testing sitekey but have not used its matching secret.", null) },
        { SitekeySecretMismatch, new(SitekeySecretMismatch, "The sitekey is not registered with the provided secret.", null) },
    };

    private readonly string? _FriendlyDescription;

    public HCaptchaErrorDetails(string errorCode, string description, string? friendlyDescription)
    {
        ErrorCode = errorCode;
        Description = description;
        _FriendlyDescription = friendlyDescription;
    }

    public string ErrorCode { get; }
    public string Description { get; }
    public string FriendlyDescription => _FriendlyDescription ?? Description;

    public static HCaptchaErrorDetails GetValue(string key)
    {
        if (_ErrorCodeDescriptionDictionary.TryGetValue(key, out HCaptchaErrorDetails? errorDetails))
        {
            return errorDetails;
        }
        else
        {
            throw new KeyNotFoundException("Error not found for details lookup");
        }
    }

    public static bool TryGetValue(string key, [MaybeNullWhen(false)] out HCaptchaErrorDetails value)
    {
        return _ErrorCodeDescriptionDictionary.TryGetValue(key, out value);
    }
}
