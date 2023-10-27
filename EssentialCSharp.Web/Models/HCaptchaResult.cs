using System.Text.Json.Serialization;

namespace EssentialCSharp.Web.Models;

public class HCaptchaResult
{
    public bool Success { get; set; }
    public DateTime ChallengeTimeStamp { get; set; }
    public string Hostname { get; set; } = string.Empty;
    [JsonPropertyName("error-codes")]
    public List<string> ErrorCodes { get; set; } = new(); 
}
