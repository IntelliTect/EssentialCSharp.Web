using System.Text;

namespace EssentialCSharp.Web.Extensions;

public static class StringExtensions
{
    // Removes special characters, sets to lowercase, replaces ' ' and '_' with '-', and trims the string
    public static IEnumerable<string> GetPotentialMatches(this string str)
    {
        string[] temp = str.Split("#");
        if (temp.Length > 1) yield return string.Join("", temp.Take(temp.Length - 1)).Sanitize();

        yield return str.Sanitize();
    }

    public static string Sanitize(this string str)
    {
        str = str.ToLowerInvariant().Trim();
        StringBuilder sb = new();
        const char separatorCharacter = '-';
        bool allowSeparator = false;
        foreach (char character in str)
        {
            switch (character)
            {
                // this second '-' here is different than a normal - in terms of key code
                // so we replace it with a normal -
                case char c when (c == '_' || c == ' ' || c == '–' || c == '-'):
                    if (allowSeparator)
                    {
                        sb.Append(separatorCharacter);
                        allowSeparator = false;
                    }
                    break;
                case char c when (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || c == '.':
                    sb.Append(character);
                    allowSeparator = true;
                    break;
                default:
                    break;
            }
        }
        return sb.ToString().TrimEnd(separatorCharacter);
    }

    // Makes a heading key (ex: hello-world) good for a heading display (ex: Hello World)
    public static string KeyToHeading(this string str)
    {
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(str.Trim().ToLowerInvariant().Replace('-', ' '));
    }
}
