using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Areas.Identity.Services.PasswordValidators;

public class PasswordLists
{
    private const string Prefix = @"Areas\Identity\Services\PasswordValidators\PasswordLists";

    public PasswordLists()
    {
        Top100000PasswordsPlus = new Lazy<HashSet<string>>(() => LoadPasswordList("Top100000CommonPasswordsPlus.txt"));
    }

    public Lazy<HashSet<string>> Top100000PasswordsPlus { get; }

    private static HashSet<string> LoadPasswordList(string listName)
    {
        // Only store in memory common passwords that are actually possible for a user to enter
        // based on our current password requirements
        return new HashSet<string>(File.ReadLines(Path.Join(Prefix, listName))
            .Where(password => password.Length >= PasswordRequirementOptions.PasswordMinimumLength
            && password.Length <= PasswordRequirementOptions.PasswordMaximumLength
            && password.Distinct().Count() >= PasswordRequirementOptions.RequiredUniqueChars));
    }
}
