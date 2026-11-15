using EssentialCSharp.Chat.Common.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// The chat widget reads <c>window.CHAT_SUPPORTS_CONTEXTUAL_SEARCH</c> to decide whether to request
/// contextual search; the API rejects that request for backends that cannot perform it.
/// </summary>
public class ChatContextualSearchCapabilityTests : IntegrationTestBase
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Layout_RendersChatBackendContextualSearchSupport(bool supportsContextualSearch)
    {
        await using WebApplicationFactory<Program> factory = Factory.Inner.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ChatBackendCapabilities>();
                services.AddSingleton(new ChatBackendCapabilities(supportsContextualSearch));
            }));
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using HttpResponseMessage response = await GetFollowingGetRedirectsAsync(client, "/home");
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(body).Contains(
            $"window.CHAT_SUPPORTS_CONTEXTUAL_SEARCH = {(supportsContextualSearch ? "true" : "false")};");
    }

    [Test]
    public async Task Layout_WithDefaultTestBackend_DoesNotRequestContextualSearch()
    {
        // The test host has no AI configuration, so the Unavailable backend (no contextual search) is selected.
        using HttpClient client = CreateClientWithoutAutoRedirect();

        using HttpResponseMessage response = await GetFollowingGetRedirectsAsync(client, "/home");
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(body).Contains("window.CHAT_SUPPORTS_CONTEXTUAL_SEARCH = false;");
    }
}
