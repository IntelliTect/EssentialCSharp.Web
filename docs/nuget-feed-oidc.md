# Private NuGet feed access with OIDC instead of a PAT

The main build (`build-and-test` in `.github/workflows/Build-Test-And-Deploy.yml`) restores
`ContentFeedNuget` from the private Azure Artifacts feed
`https://pkgs.dev.azure.com/intelliTect/_packaging/EssentialCSharp/nuget/v3/index.json`.

It can authenticate two ways:

| Mode | When | Credential |
|---|---|---|
| `oidc` | The `BuildAndUploadImage` environment has a variable `AZURE_DEVOPS_FEED_CLIENT_ID` | GitHub OIDC → `azure/login` → Entra access token for Azure DevOps (resource `499b84ac-1321-427f-aa17-267ca6975798`), valid about an hour |
| `pat` (fallback) | That variable is not set | The long-lived `AZURE_DEVOPS_PAT` repository secret |

The job logs `Private feed auth mode: oidc` or `pat` in the **Write NuGet feed credentials** step.
`scripts/nuget/get-feed-token.sh` returns the token and `scripts/nuget/emit-creds-config.sh` writes it
into a credentials-only `NuGet.Config` (for the host restore and the Docker `nuget_config` secret).

## One-time setup (owner, by hand)

1. **Create an identity** in the Entra tenant your Azure DevOps organization `intelliTect` is connected to
   (Azure DevOps → Organization settings → Microsoft Entra). Use a user-assigned managed identity or an
   app registration, for example `ecs-web-feed-reader`. Give it **no Azure RBAC roles**: it only reads the feed.
   Don't reuse `ecs-web-deploy-dev-identity` or `ecs-web-deploy-prod-identity`, which can push to ACR.
2. **Add a federated credential** to that identity:
   - Issuer: `https://token.actions.githubusercontent.com`
   - Subject: `repo:IntelliTect/EssentialCSharp.Web:environment:BuildAndUploadImage`
   - Audience: `api://AzureADTokenExchange`
3. **Add the identity to Azure DevOps**: Organization settings → Users → Add users → pick the service
   principal or managed identity. Basic access is the safe choice; Stakeholder access may not be enough to
   read Artifacts feeds.
4. **Grant feed read**: Artifacts → `EssentialCSharp` feed → Feed settings → Permissions → Add users/groups →
   the identity, role **Feed Reader**. Nothing higher.
5. **Turn it on**: GitHub → Settings → Environments → `BuildAndUploadImage` → Environment variables →
   add `AZURE_DEVOPS_FEED_CLIENT_ID` = the identity's client (application) ID. The job reuses the existing
   `AZURE_TENANT_ID` secret; if the Azure DevOps organization is in a different tenant, change the workflow's
   `tenant-id` to that tenant.

## Prove it, then retire the PAT

1. Merge to `main` (or wait for the next merge) and check that `Build and test (Ubuntu)` logs
   `Private feed auth mode: oidc`, restores `ContentFeedNuget`, and that the container build succeeds.
2. If it fails, delete the `AZURE_DEVOPS_FEED_CLIENT_ID` variable: the next run falls back to the PAT.
3. Once it has worked on `main`:
   - Remove the PAT fallback (the `pat` branch of `get-feed-token.sh` and the `AZURE_DEVOPS_PAT` env lines
     in `build-and-test`).
   - Move the other PAT users first, or keep a PAT just for them:
     - `.github/dependabot.yml` registry `nuget-azure-artifacts`. Dependabot supports Azure OIDC for
       private registries (`tenant-id` and `client-id` instead of `username`/`password`); follow GitHub's
       Dependabot OIDC docs for the federated-credential subject it uses.
     - `.github/workflows/copilot-setup-steps.yml` and the `copilot` environment's own `AZURE_DEVOPS_PAT`.
   - Delete the `AZURE_DEVOPS_PAT` secrets and revoke the PAT in Azure DevOps.
