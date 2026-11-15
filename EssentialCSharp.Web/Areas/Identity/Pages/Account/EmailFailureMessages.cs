using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

/// <summary>
/// User-facing text for when an account email could not be sent (<see cref="Services.EmailSendException"/>).
/// The "Error" prefix makes the shared status-message partial render these as errors.
/// </summary>
internal static class EmailFailureMessages
{
    /// <summary>For flows where the account already exists and the user can request the email again.</summary>
    public const string AccountCreatedConfirmationNotSent =
        "Error: Your account was created, but we couldn't send the confirmation email right now. Please request a new confirmation email below.";

    /// <summary>TempData key read by <see cref="ResendEmailConfirmationModel.StatusMessage"/>.</summary>
    public const string ResendStatusMessageKey = nameof(ResendEmailConfirmationModel.StatusMessage);

    /// <summary>Queues <see cref="AccountCreatedConfirmationNotSent"/> for the ResendEmailConfirmation page to display.</summary>
    public static void QueueAccountCreatedConfirmationNotSent(ITempDataDictionary tempData) =>
        tempData[ResendStatusMessageKey] = AccountCreatedConfirmationNotSent;

    /// <summary>For signed-in account management pages.</summary>
    public const string TryAgainLater =
        "Error: We couldn't send the email right now. Please try again in a few minutes.";

    /// <summary>
    /// For anonymous pages where a different response for an existing account would reveal that it exists.
    /// Intentionally identical whether or not the account exists or the email was sent.
    /// </summary>
    public const string ResendNeutral =
        "If an account exists for that email address, we've sent a verification email. Please check your email, including your spam folder. If it doesn't arrive in a few minutes, try again.";
}
