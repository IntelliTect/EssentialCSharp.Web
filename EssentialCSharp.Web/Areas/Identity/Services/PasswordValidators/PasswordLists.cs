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
        HashSet<string> hashset = new(File.ReadLines(Path.Join(Prefix, listName)));

        return hashset;
    }
}
