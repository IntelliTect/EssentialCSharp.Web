using System.Text;

namespace EssentialCSharp.Web.Extensions;

public static class StringExtensions
{
    // Removes special characters, sets to lowercase, replaces ' ' and '_' with '-', and trims the string
    public static string SanitizeKey(this string str)
    {
        str = str.ToLowerInvariant().Trim();
        StringBuilder sb = new();
        foreach (char character in str)
        {
            switch (character)
            {
                // this second '-' here is different than a normal - in terms of key code
                // so we replace it with a normal -
                case char c when c == '_' || c == ' ' || c == '–':
                    sb.Append('-');
                    break;
                case char r2d2 when (r2d2 >= '0' && r2d2 <= '9') || (r2d2 >= 'a' && r2d2 <= 'z') || r2d2 == '.' || r2d2 == '-':
                    sb.Append(character);
                    break;
                default:
                    break;
            }
        }
        return sb.ToString();
    }

    // Makes a heading key (ex: hello-world) good for a heading display (ex: Hello World)
    public static string KeyToHeading(this string str)
    {
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(str.Trim().ToLowerInvariant().Replace('-', ' '));
    }
}
