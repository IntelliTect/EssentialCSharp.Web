# IndexNow Implementation

This project implements the IndexNow protocol to instantly notify search engines about new or updated content, replacing the Google sitemap ping functionality.

## What is IndexNow?

IndexNow is a protocol supported by major search engines including Bing, Seznam.cz, and Naver that allows websites to instantly notify search engines when URLs are added, updated, or deleted. This provides faster indexing compared to traditional sitemap crawling.

## Configuration

### 1. Generate an IndexNow Key

Generate a unique key (32-64 characters) for your site. This can be a UUID or any unique string:

```bash
# Example key generation
echo "$(openssl rand -hex 32)"
```

### 2. Configure appsettings.json

Add the IndexNow configuration to your `appsettings.json`:

```json
{
  "IndexNow": {
    "Key": "your-unique-32-64-character-key-here",
    "BaseUrl": "https://essentialcsharp.com"
  }
}
```

For production, it's recommended to use environment variables or Azure Key Vault:

```bash
# Environment variables
IndexNow__Key=your-unique-key-here
IndexNow__BaseUrl=https://essentialcsharp.com
```

### 3. Verify Key File Access

The IndexNow key file will be automatically served at `https://yourdomain.com/{your-key}.txt`. Search engines use this to verify ownership.

## Available Endpoints

### XML Sitemap
- **URL**: `/sitemap.xml`
- **Method**: GET
- **Description**: Generates XML sitemap from existing site mapping data
- **Cache**: 1 hour response cache

### IndexNow Key File
- **URL**: `/{key}.txt`
- **Method**: GET
- **Description**: Serves the IndexNow key for verification
- **Example**: `/your-unique-key.txt`

### Manual Notification Trigger
- **URL**: `/api/notify-indexnow`
- **Method**: POST
- **Description**: Manually triggers IndexNow notifications for all sitemap URLs
- **Use**: For testing or manual content updates

## Programmatic Usage

### Notify Single URL
```csharp
// Inject IServiceProvider into your service/controller
await serviceProvider.NotifyIndexNowAsync("your-page-url");
```

### Notify Multiple URLs
```csharp
var urls = new[] { "page1", "page2", "page3" };
await serviceProvider.NotifyIndexNowAsync(urls);
```

### Notify All Sitemap URLs
```csharp
await serviceProvider.NotifyAllSitemapUrlsAsync();
```

## Integration with Content Updates

You can integrate IndexNow notifications into your content management workflow:

```csharp
public class ContentService
{
    private readonly IServiceProvider _serviceProvider;
    
    public ContentService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    public async Task UpdateContentAsync(string pageUrl)
    {
        // Update your content
        UpdateContent(pageUrl);
        
        // Notify search engines
        await _serviceProvider.NotifyIndexNowAsync(pageUrl);
    }
}
```

## Search Engine Support

The implementation automatically notifies the following search engines:

- **Generic IndexNow API**: `https://api.indexnow.org/IndexNow`
- **Bing**: `https://www.bing.com/IndexNow`
- **Seznam.cz**: `https://search.seznam.cz/IndexNow`
- **Naver**: `https://searchadvisor.naver.com/indexnow`

Note: Google does not support IndexNow and uses its own indexing API.

## Error Handling

The implementation includes comprehensive error handling:

- Network failures are logged but don't stop execution
- Invalid configurations are handled gracefully
- Each search engine is notified independently

## Testing

Use the manual trigger endpoint to test the implementation:

```bash
curl -X POST https://essentialcsharp.com/api/notify-indexnow
```

Check your server logs to confirm notifications are being sent to search engines.

## Best Practices

1. **Generate a unique key** for each domain
2. **Store keys securely** using environment variables or key management
3. **Monitor logs** to ensure notifications are successful
4. **Don't over-notify** - only trigger when content actually changes
5. **Test thoroughly** before deploying to production

## Migration from Google Sitemap Ping

This implementation replaces Google sitemap ping because:

- Google deprecated sitemap ping functionality
- IndexNow provides faster, more reliable notifications
- Better support from multiple search engines
- Modern, standardized protocol

The existing `robots.txt` still references `sitemap.xml`, ensuring compatibility with search engines that don't support IndexNow.