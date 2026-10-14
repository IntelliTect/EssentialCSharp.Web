# Essential C# Web Project

## Projects Overview

- [EssentialCSharp.Web](https://github.com/IntelliTect/EssentialCSharp.Web/tree/main/EssentialCSharp.Web) - The site seen at [essentialcsharp.com](https://essentialcsharp.com/)

For any bugs, questions, or anything else with specifically the code found inside the listings (listing examples code), please submit an issue at the [EssentialCSharp Repo](https://github.com/IntelliTect/EssentialCSharp).

## Getting Started

For information on setting up your local development environment, please see the [Getting Started Guide](docs/getting-started.md).

Please use issues or discussions to report issues found.

## IndexNow Setup (Production Deployment)

The deployment workflow notifies search engines (Bing, Yandex, Naver) of content updates via the [IndexNow protocol](https://www.indexnow.org/). After each successful production deploy it submits only the URLs that were added, updated, or removed in that deploy.

### How It Works

1. Before deploying, the workflow snapshots the live `https://essentialcsharp.com/sitemap.xml`.
2. After the smoke test confirms the new version is live, it fetches the sitemap again.
3. It diffs the two: URLs that were added, removed, or whose `<lastmod>` changed are POSTed to `https://api.indexnow.org/indexnow`. If nothing changed, nothing is submitted.
4. IndexNow distributes those URLs to all participating search engines (Bing, Yandex, Naver, etc.).

Book pages get their `<lastmod>` from `sitemap.json` in the content NuGet package. Static app pages (`/about`, `/guidelines`, etc.) have no `<lastmod>`, so they are only submitted when added or removed.

### Setup Requirements

#### 1. GitHub Secret
Generate a random hex key and store it in GitHub repository secrets as `INDEXNOW_API_KEY`. The workflow uses this value both to write the key verification file into the Docker image at build time and to authenticate IndexNow submissions.

Go to: **GitHub repo → Settings → Secrets and variables → Actions → New repository secret**
- Name: `INDEXNOW_API_KEY`
- Value: a random hex string (e.g. generate with `openssl rand -hex 16`)

#### 2. Key Verification File (auto-generated at build time)
IndexNow requires domain ownership proof via a publicly accessible `.txt` file:
- The workflow writes `wwwroot/{key}.txt` (containing the key value) before building the Docker image
- The file is NOT committed to git — it is generated fresh on every CI run from the GitHub Secret
- ASP.NET Core's static file middleware serves it at `https://essentialcsharp.com/{key}.txt`
- IndexNow crawls that URL to verify domain ownership before accepting submissions
- Without this file, all submissions return HTTP 403

The key file is intentionally public — that is by design. The "security" is that only the domain owner can host a file at that path.

### Important Notes
- The IndexNow step uses `continue-on-error: true` — a submission failure will never block a deployment
- Only changed URLs are submitted, per the IndexNow guidance; resubmitting unchanged URLs on every deploy risks submissions being deprioritized
- To rotate the key: generate a new hex string and update the GitHub Secret — the old key file disappears automatically on the next deploy
