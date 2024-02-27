using System.Text.Json.Serialization;

namespace EssentialCSharp.Web.Models;

// https://docs.hcaptcha.com/#verify-the-user-response-server-side
//{
//   "success": true|false,     // is the passcode valid, and does it meet security criteria you specified, e.g. sitekey?
//   "challenge_ts": timestamp, // timestamp of the challenge (ISO format yyyy-MM-dd'T'HH:mm:ssZZ)
//   "hostname": string,        // the hostname of the site where the challenge was solved
//   "credit": true|false,      // optional: deprecated field
//   "error-codes": [...]       // optional: any error codes
//   "score": float,            // ENTERPRISE feature: a score denoting malicious activity.
//   "score_reason": [...]      // ENTERPRISE feature: reason(s) for score.
//}
public class HCaptchaResult
{

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("challenge_ts")]
    public DateTimeOffset ChallengeTimeStamp { get; set; }

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = string.Empty;

    [JsonPropertyName("credit")]
    public bool Credit { get; set; }

    [JsonPropertyName("error-codes")]
    public string[]? ErrorCodes { get; set; }
}
