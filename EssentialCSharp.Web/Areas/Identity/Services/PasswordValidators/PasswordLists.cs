using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Areas.Identity.Services.PasswordValidators
{
    public class PasswordLists
    {
        private const string Prefix = @"Areas\Identity\Services\PasswordValidators\PasswordLists";

        //private readonly int _requiredLength;
        //private readonly ILogger<PasswordLists> _logger;

        public PasswordLists()
        {
            //_requiredLength = options.Value.Password.RequiredLength;
            //_logger = logger;
            Top100000PasswordsPlus = new Lazy<HashSet<string>>(() => LoadPasswordList("Top100000CommonPasswordsPlus.txt"));
        }

        public Lazy<HashSet<string>> Top100000PasswordsPlus { get; }

        private static HashSet<string> LoadPasswordList(string listName)
        {
            HashSet<string> hashset = new(File.ReadLines(Path.Join(Prefix, listName)));

            //_logger.LogDebug("Loaded {NumberCommonPasswords} common passwords from resource {ResourceName}", hashset.Count, listName);
            return hashset;
        }

        private static IEnumerable<string> GetLines(StreamReader reader)
        {
            while (!reader.EndOfStream)
            {
                if (reader.ReadLine() is string line)
                {
                    yield return line;
                }
            }
        }
    }
}
