namespace EssentialCSharp.Web.Services;

public class SearchIndexingHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SearchIndexingHostedService> _logger;

    public SearchIndexingHostedService(IServiceProvider serviceProvider, ILogger<SearchIndexingHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting search indexing service");

        // Use a background task to avoid blocking startup
        _ = Task.Run(async () =>
        {
            try
            {
                // Wait a bit for the application to fully start
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

                using var scope = _serviceProvider.CreateScope();
                var searchService = scope.ServiceProvider.GetRequiredService<ITypesenseSearchService>();
                var indexingService = scope.ServiceProvider.GetRequiredService<IContentIndexingService>();

                // Check if Typesense is healthy
                var isHealthy = await searchService.IsHealthyAsync(cancellationToken);
                if (!isHealthy)
                {
                    _logger.LogWarning("Typesense is not healthy, skipping content indexing");
                    return;
                }

                // Index all content
                var success = await indexingService.IndexAllContentAsync(cancellationToken);
                if (success)
                {
                    _logger.LogInformation("Successfully completed content indexing");
                }
                else
                {
                    _logger.LogError("Content indexing failed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during content indexing");
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping search indexing service");
        return Task.CompletedTask;
    }
}