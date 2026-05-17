# Essential C# Web Project

## Projects Overview

- [EssentialCSharp.Web](https://github.com/IntelliTect/EssentialCSharp.Web/tree/main/EssentialCSharp.Web) - The site seen at [essentialcsharp.com](https://essentialcsharp.com/)

For any bugs, questions, or anything else with specifically the code found inside the listings (listing examples code), please submit an issue at the [EssentialCSharp Repo](https://github.com/IntelliTect/EssentialCSharp).

## Getting Started

For information on setting up your local development environment, please see the [Getting Started Guide](docs/getting-started.md).

Please use issues or discussions to report issues found.

## IndexNow Setup (Production Deployment)

The deployment workflow notifies search engines (Bing, Yandex, Naver) of content updates via the [IndexNow protocol](https://www.indexnow.org/). To enable this:

### Key File Requirement
IndexNow requires verification that you own the domain by hosting your API key as a publicly accessible `.txt` file:

1. **Create the key file**: Place your IndexNow API key in `EssentialCSharp.Web/wwwroot/{INDEXNOW_API_KEY}.txt`
   - File content should contain **only** the key value (no whitespace, no newlines)
   - Example: if key is `abc123def456`, the file is `abc123def456.txt` containing exactly that string

2. **Deploy as static asset**: Ensure the key file is deployed with the application so it's accessible at:
   ```
   https://essentialcsharp.com/{INDEXNOW_API_KEY}.txt
   ```

3. **Set GitHub Secret**: Store your IndexNow API key in GitHub repository secrets as `INDEXNOW_API_KEY`
   - Do NOT commit the literal key to the repository

### Important Notes
- Without the verification file, IndexNow submissions will be rejected with HTTP 403
- The workflow submits `sitemap.xml` to IndexNow; for more granular control, consider extracting individual changed URLs and submitting those instead
- The IndexNow step uses `continue-on-error: true` to prevent deployment failures if the notification fails
