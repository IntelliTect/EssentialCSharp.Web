# Typesense Search Integration

This document describes the integration of Typesense search engine as a replacement for Algolia DocSearch in the EssentialCSharp.Web application.

## Overview

Typesense is an open-source, fast search engine that provides:
- **Full-text search** with typo tolerance
- **Faceted search** capabilities  
- **Real-time indexing** of content
- **Self-hosted solution** - no external dependencies
- **Fast search responses** with sub-50ms latency

## Architecture

The integration consists of several components:

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

### Docker Configuration

The `docker-compose.yml` includes:
- **Typesense service** running on port 8108
- **PostgreSQL** for existing vector database functionality
- **Main web application** with environment variables for Typesense connection

## Configuration

### Application Settings

Add to `appsettings.json`:

```json
{
  "TypesenseOptions": {
    "Host": "localhost",
    "Port": 8108,
    "Protocol": "http",
    "ApiKey": "your-api-key-here",
    "TimeoutSeconds": 30
  }
}
```

### Environment Variables (Docker)

- `TYPESENSE_API_KEY`: API key for Typesense authentication

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

## Deployment with Docker Compose

1. **Start the services**:
   ```bash
   docker-compose up -d
   ```

2. **Set environment variables**:
   ```bash
   export TYPESENSE_API_KEY="your-secure-api-key"
   ```

3. **Verify Typesense health**:
   ```bash
   curl http://localhost:8108/health
   ```

4. **Check search functionality**:
   ```bash
   curl "http://localhost:8080/api/search?q=variables"
   ```

## Container App Deployment (Azure)

For Azure Container Apps deployment:

1. **Create Typesense container**:
   ```bash
   az containerapp create \
     --name typesense-search \
     --resource-group myResourceGroup \
     --environment myEnvironment \
     --image typesense/typesense:0.25.2 \
     --target-port 8108 \
     --env-vars API_KEY=secretref:typesense-api-key \
     --secrets typesense-api-key=your-secure-key
   ```

2. **Update main app configuration**:
   ```bash
   az containerapp update \
     --name essentialcsharp-web \
     --set-env-vars TypesenseOptions__Host=typesense-search \
                    TypesenseOptions__ApiKey=secretref:typesense-api-key
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
- **Memory usage**: Typesense uses in-memory indexing for fast searches
- **Rate limiting**: Prevents search abuse and ensures fair usage

## Monitoring and Health Checks

- Built-in health check endpoint: `/api/search/health`
- Typesense health endpoint: `http://typesense:8108/health`
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
   - Check if Typesense container is running
   - Verify network connectivity between containers
   - Ensure API key is correct

2. **Search returns no results**:
   - Check if content indexing completed successfully
   - Verify collection exists: `curl http://typesense:8108/collections`
   - Check application logs for indexing errors

3. **Slow search performance**:
   - Monitor Typesense memory usage
   - Consider increasing container resources
   - Check network latency between services

### Logging

Search operations are logged with structured data:
- Search queries and response times
- Indexing operations and document counts
- Error conditions and health check results

## Future Enhancements

Potential improvements:
- **Autocomplete suggestions** based on popular searches
- **Search analytics** and query optimization
- **Advanced filters** for content types and difficulty levels
- **Personalized search** based on user preferences
- **Multi-language support** for internationalization