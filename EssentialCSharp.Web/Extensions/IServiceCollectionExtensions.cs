using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Extensions;

public static class IServiceCollectionExtensions
{
    public static void AddCaptchaService(this IServiceCollection services, IConfigurationSection CaptchaOptions)
    {
        services.Configure<CaptchaOptions>(CaptchaOptions);
        services.AddSingleton<ICaptchaService, CaptchaService>();
        services.AddHttpClient("hCaptcha", c =>
        {
            c.BaseAddress = new Uri("https://api.hcaptcha.com");
        });
    }
}
