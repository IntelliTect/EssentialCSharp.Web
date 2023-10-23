namespace EssentialCSharp.Web.Services
{
    public class AuthMessageSenderOptions
    {
        public const string AuthMessageSender = "AuthMessageSender";
        public string? APIKey { get; set; }
        public string? SecretKey { get; set; }
        public string? SendFromEmail { get; set; }
        public string? SendFromName { get; set; }
    }
}
