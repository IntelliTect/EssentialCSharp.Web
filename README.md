# Essential C# Web Project

## Projects Overview

- [EssentialCSharp.Web](https://github.com/IntelliTect/EssentialCSharp.Web/tree/main/EssentialCSharp.Web) - The site seen at [essentialcsharp.com](https://essentialcsharp.com/)

For any bugs, questions, or anything else with specifically the code found inside the listings (listing examples code), please submit an issue at the [EssentialCSharp Repo](https://github.com/IntelliTect/EssentialCSharp).

## Getting Started

For information on setting up your local development environment, please see the [Getting Started Guide](docs/getting-started.md).

Please use issues or discussions to report issues found.

## IndexNow Setup (Production Deployment)

The deployment workflow notifies search engines (Bing, Yandex, Naver) of content updates via the [IndexNow protocol](https://www.indexnow.org/). After each successful production deploy the workflow fetches the live `sitemap.xml`, extracts all content URLs, and submits them in a single batch POST to IndexNow.

### How It Works

1. After the smoke test confirms the app is live, the workflow fetches `https://essentialcsharp.com/sitemap.xml`.
2. All `<loc>` URLs are extracted and POSTed to `https://api.indexnow.org/indexnow`.
3. IndexNow distributes those URLs to all participating search engines (Bing, Yandex, Naver, etc.).

### Setup Requirements

#### 1. Key Verification File (already committed)
IndexNow requires domain ownership proof via a publicly accessible `.txt` file at the root of the domain:
- The key file lives at `EssentialCSharp.Web/wwwroot/{key}.txt`
- The filename and file content are the same value (the key itself)
- ASP.NET Core's static file middleware serves it at `https://essentialcsharp.com/{key}.txt`
- IndexNow crawls that URL to verify domain ownership before accepting submissions
- Without this file, all submissions return HTTP 403

The key file is intentionally public — that is by design. The "security" is that only the domain owner can host a file at that path.

#### 2. GitHub Secret
Store the key value in GitHub repository secrets as `INDEXNOW_API_KEY`. The workflow reads it from there — the secret value must match the key filename/content committed to the repo.

Go to: **GitHub repo → Settings → Secrets and variables → Actions → New repository secret**
- Name: `INDEXNOW_API_KEY`
- Value: the key string (same value as the `.txt` filename in `wwwroot/`)

### Important Notes
- The IndexNow step uses `continue-on-error: true` — a submission failure will never block a deployment
- Submitting all sitemap URLs on every deploy is intentional; IndexNow has no hard rate limit for batch submissions and the full URL list ensures nothing is missed
- To rotate the key: generate a new hex string, add a new `wwwroot/{newkey}.txt`, update the GitHub Secret, and remove the old `.txt` file
