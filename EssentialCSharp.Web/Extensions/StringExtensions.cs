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
                case char c when c == '_' || c == ' ':
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
}
