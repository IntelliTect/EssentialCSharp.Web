using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EssentialCSharp.Web.AppHost.Tests.Journeys;

/// <summary>
/// Replaces hCaptcha on both sides so journeys don't depend on hcaptcha.com: a browser script that solves the
/// invisible challenge immediately, and an in-process <c>/siteverify</c> that accepts every token.
/// </summary>
public sealed class FakeHCaptcha : IAsyncDisposable
{
    private const string Token = "10000000-aaaa-bbbb-cccc-000000000001";

    // Implements the subset of the hCaptcha JS API the site uses (explicit render, invisible execute, onload callback).
    public const string BrowserScript = $$"""
        (() => {
            const widgets = [];
            window.hcaptcha = {
                render(container, options) {
                    const element = typeof container === 'string' ? document.getElementById(container) : container;
                    widgets.push({ element, options });
                    return widgets.length - 1;
                },
                execute(id) {
                    const { element, options } = widgets[id ?? 0];
                    let field = element.querySelector('[name="h-captcha-response"]');
                    if (!field) {
                        field = document.createElement('textarea');
                        field.name = 'h-captcha-response';
                        field.hidden = true;
                        element.appendChild(field);
                    }
                    field.value = '{{Token}}';
                    setTimeout(() => options.callback?.('{{Token}}'), 0);
                },
                reset() {},
                getResponse() { return '{{Token}}'; },
            };
            const onload = new URL(document.currentScript.src).searchParams.get('onload');
            if (onload && typeof window[onload] === 'function') {
                window[onload]();
            }
        })();
        """;

    private readonly WebApplication _app;

    private FakeHCaptcha(WebApplication app, Uri apiBaseUrl)
    {
        _app = app;
        ApiBaseUrl = apiBaseUrl;
    }

    /// <summary>Value for the web app's <c>HCaptcha:ApiBaseUrl</c>.</summary>
    public Uri ApiBaseUrl { get; }

    public static async Task<FakeHCaptcha> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        WebApplication app = builder.Build();
        app.MapPost("/siteverify", () => Results.Json(new { success = true, hostname = "localhost" }));
        await app.StartAsync();

        string address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        return new FakeHCaptcha(app, new Uri(address));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
