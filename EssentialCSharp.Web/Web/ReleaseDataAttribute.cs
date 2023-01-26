using System.Globalization;
using System.Reflection;

namespace EssentialCSharp.Web;

[AttributeUsage(AttributeTargets.Assembly)]
public class ReleaseDateAttribute : Attribute
{
    public ReleaseDateAttribute(string date)
    {
        ReleaseDate = DateTime.ParseExact(date, "O", CultureInfo.InvariantCulture);
    }
    public DateTime ReleaseDate { get; }
    public static DateTime? GetReleaseDate(Assembly? assembly = null)
    {
        object[]? attribute = (assembly ?? Assembly.GetEntryAssembly())?.GetCustomAttributes(typeof(ReleaseDateAttribute), false);
        return attribute?.Length >= 1 ? ((ReleaseDateAttribute)attribute[0]).ReleaseDate : default;
    }
}
