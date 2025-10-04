# Typesense Search Integration for Azure Container Apps

This document describes the integration of Typesense search engine as a replacement for Algolia DocSearch in the EssentialCSharp.Web application, designed for deployment in Azure Container Apps.

## Overview

Typesense is an open-source, fast search engine that provides:
- **Full-text search** with typo tolerance
- **Faceted search** capabilities  
- **Real-time indexing** of content
- **Self-hosted solution** - no external dependencies
- **Fast search responses** with sub-50ms latency

## Architecture for Azure Container Apps

The integration consists of several components designed to work in Azure Container Apps:

### Backend Services

1. **TypesenseSearchService** (`Services/TypesenseSearchService.cs`)
   - HTTP client-based implementation for Typesense API
   - Handles collection creation, document indexing, and search operations
   - Uses configuration from `TypesenseOptions`

2. **ContentIndexingService** (`Services/ContentIndexingService.cs`)
   - Extracts content from HTML files based on site mappings
   - Processes and cleans text content for indexing
   - Bulk indexes all site content

3. **SearchIndexingHostedService** (`Services/SearchIndexingHostedService.cs`)
   - Background service that automatically indexes content on application startup
   - Waits for Typesense to be healthy before indexing

4. **SearchController** (`Controllers/SearchController.cs`)
   - REST API endpoints for search operations
   - Rate-limited to prevent abuse
   - Supports both GET and POST search methods

### Frontend Components

1. **TypesenseSearch JavaScript Module** (`wwwroot/js/typesenseSearch.js`)
   - Modern ES6 module for search functionality
   - Modal-based search interface
   - Debounced search with keyboard shortcuts (Ctrl+K)
   - Real-time search results with highlighting

2. **Search Styles** (`wwwroot/css/typesense-search.css`)
   - Responsive modal design
   - Dark mode support
   - Loading states and error handling

## Configuration

### Application Settings

Add to `appsettings.json`:

```json
{
  "TypesenseOptions": {
    "Host": "your-typesense-container-app.azurecontainerapps.io",
    "Port": 443,
    "Protocol": "https",
    "ApiKey": "your-api-key-here",
    "TimeoutSeconds": 30
  }
}
```

### Environment Variables (Azure Container Apps)

The Typesense configuration should be added to the deployment pipeline:

```bash
# Add to the az containerapp secret set command:
typesense-apikey=keyvaultref:$KEYVAULTURI/secrets/typesense-apikey,identityref:$MANAGEDIDENTITYID \
typesense-host=keyvaultref:$KEYVAULTURI/secrets/typesense-host,identityref:$MANAGEDIDENTITYID

# Add to the az containerapp update --replace-env-vars command:
TypesenseOptions__Host=secretref:typesense-host \
TypesenseOptions__ApiKey=secretref:typesense-apikey \
TypesenseOptions__Port=443 \
TypesenseOptions__Protocol=https
```

### Rate Limiting

Search endpoints are rate-limited:
- **50 requests per minute** for search operations
- Uses partition-based limiting (by user or IP)

## API Endpoints

### Search Content
```
GET /api/search?q={query}&page={page}&perPage={perPage}
POST /api/search
```

### Health Check
```
GET /api/search/health
```

## Data Model

### Search Document Structure
```json
{
  "id": "unique-identifier",
  "title": "Page title",
  "content": "Extracted text content",
  "url": "/page-url#anchor",
  "chapter": "Chapter 1: Introduction",
  "section": "Section title",
  "tags": ["chapter-1"],
  "created_at": 1640995200
}
```

### Typesense Collection Schema
- **id**: string (primary key)
- **title**: string (searchable)
- **content**: string (searchable) 
- **url**: string
- **chapter**: string (faceted)
- **section**: string (faceted)
- **tags**: string array (faceted)
- **created_at**: int64 (sorting field)

## Deployment in Azure Container Apps

### Step 1: Deploy Typesense Container App

Create a separate Container App for Typesense:

```bash
# Create Typesense Container App
az containerapp create \
  --name typesense-search \
  --resource-group $RESOURCEGROUP \
  --environment $CONTAINER_APP_ENVIRONMENT \
  --image typesense/typesense:0.25.2 \
  --target-port 8108 \
  --ingress external \
  --env-vars TYPESENSE_API_KEY=secretref:typesense-apikey \
  --secrets typesense-apikey="your-secure-api-key" \
  --command '--data-dir /data --api-key=$TYPESENSE_API_KEY --enable-cors' \
  --cpu 1.0 \
  --memory 2Gi
```

### Step 2: Update Main Application Deployment

Update the existing deployment pipeline (`Build-Test-And-Deploy.yml`) to include Typesense configuration:

1. **Add Typesense secrets to Key Vault**:
   - `typesense-apikey`: Your secure API key
   - `typesense-host`: The Typesense Container App URL

2. **Update the pipeline secrets configuration**:
   ```bash
   # Add to the existing secret set command around line 154:
   typesense-apikey=keyvaultref:$KEYVAULTURI/secrets/typesense-apikey,identityref:$MANAGEDIDENTITYID \
   typesense-host=keyvaultref:$KEYVAULTURI/secrets/typesense-host,identityref:$MANAGEDIDENTITYID
   ```

3. **Update the environment variables around line 160**:
   ```bash
   # Add to the existing environment variables:
   TypesenseOptions__Host=secretref:typesense-host \
   TypesenseOptions__ApiKey=secretref:typesense-apikey \
   TypesenseOptions__Port=443 \
   TypesenseOptions__Protocol=https
   ```

### Step 3: Verify Deployment

1. **Check Typesense health**:
   ```bash
   curl https://your-typesense-container-app.azurecontainerapps.io/health
   ```

2. **Test search functionality**:
   ```bash
   curl "https://your-main-app.azurecontainerapps.io/api/search?q=variables"
   ```

## Features

### Search Capabilities
- **Full-text search** across title and content
- **Chapter and section filtering**
- **Typo tolerance** (up to 2 character differences)
- **Phrase matching** and partial word matching
- **Faceted search** by chapter and tags

### User Interface
- **Modal search interface** with keyboard shortcuts
- **Real-time search** with debounced input
- **Search result highlighting** 
- **Responsive design** for mobile and desktop
- **Loading states** and error handling

### Content Processing
- **HTML content extraction** from site mapping files
- **Text cleaning** and normalization
- **Automatic indexing** on application startup
- **Incremental updates** support

## Performance Considerations

- **Search latency**: Typically under 50ms for most queries
- **Index size**: Automatically managed by Typesense
- **Memory usage**: Recommend 2GB+ for Typesense Container App
- **Rate limiting**: Prevents search abuse and ensures fair usage

## Monitoring and Health Checks

- Built-in health check endpoint: `/api/search/health`
- Typesense health endpoint: `https://your-typesense-app.azurecontainerapps.io/health`
- Structured logging for search operations and errors
- Application Insights integration for telemetry

## Migration from Algolia

The integration replaces Algolia DocSearch:
- ✅ **Self-hosted**: No external service dependencies
- ✅ **Cost-effective**: No usage-based pricing
- ✅ **Full control**: Complete control over search ranking and indexing
- ✅ **Privacy**: All data stays within your infrastructure
- ✅ **Customizable**: Easy to modify search behavior and UI

## Troubleshooting

### Common Issues

1. **Typesense connection failed**:
   - Check if Typesense Container App is running
   - Verify network connectivity between Container Apps
   - Ensure API key is correct in Key Vault

2. **Search returns no results**:
   - Check if content indexing completed successfully
   - Verify collection exists: `curl https://your-typesense-app.azurecontainerapps.io/collections`
   - Check Application Insights for indexing errors

3. **Slow search performance**:
   - Monitor Typesense Container App resource usage
   - Consider increasing CPU/memory allocation
   - Check network latency between Container Apps

### Logging

Search operations are logged with structured data:
- Search queries and response times
- Indexing operations and document counts
- Error conditions and health check results

## Cost Considerations

- **Typesense Container App**: Runs continuously, estimated cost based on CPU/memory allocation
- **Storage**: Persistent volumes for Typesense data (if needed)
- **Network**: Minimal egress costs for inter-container communication within the same region

## Future Enhancements

Potential improvements:
- **Autocomplete suggestions** based on popular searches
- **Search analytics** and query optimization
- **Advanced filters** for content types and difficulty levels
- **Personalized search** based on user preferences
- **Multi-language support** for internationalization