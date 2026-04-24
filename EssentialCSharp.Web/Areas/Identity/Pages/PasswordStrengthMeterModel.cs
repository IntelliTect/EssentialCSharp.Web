namespace EssentialCSharp.Web.Areas.Identity.Pages;

public class PasswordStrengthMeterModel(string passwordFieldId, string userInputFieldIds = "")
{
    public string PasswordFieldId { get; } = passwordFieldId;
    public string UserInputFieldIds { get; } = userInputFieldIds;
}
