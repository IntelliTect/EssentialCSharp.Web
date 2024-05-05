using System.Text;

namespace EssentialCSharp.Web.Extensions;

public static class StringExtensions
{
    /// <summary>
    /// Prepares a string to be sanitized.
    /// </summary>
    /// <param name="str">Input string</param>
    /// <returns>An IEnumerable of the input string</returns>
    public static IEnumerable<string> GetPotentialMatches(this string str)
    {
        string[] pathBeforeAnchorFragment = str.Split("#");
        if (pathBeforeAnchorFragment.Length > 1) yield return string.Join("", pathBeforeAnchorFragment.Take(pathBeforeAnchorFragment.Length - 1)).Sanitize();

        yield return str.Sanitize();
    }

    /// <summary>
    /// Prepares a string for use in a URL. Allows for a user to type in a chapter heading
    /// and have it converted to a URL friendly string that will hopefully get them to the
    /// content they want, by translating the string to hopefully a match with a key
    /// in the site mapping key. Ex: Allows https://essentialcsharp.com/All Classes Derive from System.Object
    /// be converted to https://essentialcsharp.com/All%20Classes%20Derive%20from%20System.Object
    /// </summary>
    /// <param name="str">Input string</param>
    /// <returns>String ready to match to a site mapping</returns>
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
                case char c when (c == '_' || c == ' ' || c == '–' || c == '-' || c == '.'):
                    if (allowSeparator)
                    {
                        sb.Append(separatorCharacter);
                        allowSeparator = false;
                    }
                    break;
                case char c when (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z'):
                    sb.Append(character);
                    allowSeparator = true;
                    break;
                default:
                    break;
            }
        }
        return sb.ToString().TrimEnd(separatorCharacter);
    }

    /// <summary>
    /// Makes a heading key (ex: hello-world) good for a heading display (ex: Hello World)
    /// </summary>
    /// <param name="str">Input string (ex: hello-world)</param>
    /// <returns>String appropriate for a heading (ex: Hello World)</returns>
    public static string KeyToHeading(this string str)
    {
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(str.Trim().ToLowerInvariant().Replace('-', ' '));
    }

    public static SiteMapping? Find(this string key, IList<SiteMapping> siteMappings)
    {
        key ??= siteMappings[0].Key;
        foreach (string? potentialMatch in key.GetPotentialMatches())
        {
            if (siteMappings.FirstOrDefault(x => x.Key == potentialMatch) is { } siteMap)
            {
                return siteMap;
            }
        }
        return null;
    }
}
