namespace EssentialCSharp.Web.Controllers;

public class ChatMessageResponse
{
    public string Response { get; set; } = string.Empty;
    public string ResponseId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
