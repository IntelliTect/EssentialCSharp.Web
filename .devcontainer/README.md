# DevContainer Setup for EssentialCSharp.Web

This project includes a DevContainer configuration for development with Visual Studio Code and GitHub Codespaces.

## Prerequisites

- [Visual Studio Code](https://code.visualstudio.com/) with the [Remote-Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers)
- [Docker Desktop](https://www.docker.com/products/docker-desktop) (for local development)

## Private NuGet Feed Authentication

This project uses a private Azure DevOps NuGet feed for some packages. To access these packages, you need to set up authentication.

### For GitHub Codespaces

1. Create a Personal Access Token (PAT) in Azure DevOps:
   - Go to https://dev.azure.com/intelliTect/_usersSettings/tokens
   - Click "New Token"
   - Name: "EssentialCSharp DevContainer"
   - Scopes: Select "Packaging (read)"
   - Click "Create"

2. Add the token as a Codespace secret:
   - Go to your GitHub repository settings
   - Navigate to "Codespaces" → "Repository secrets"
   - Click "New repository secret"
   - Name: `AZURE_DEVOPS_PAT`
   - Value: Your Azure DevOps PAT

### For Local Development

1. Create a Personal Access Token (PAT) in Azure DevOps (same as above)

2. Create a `.env` file in the `.devcontainer` directory:
   ```bash
   cp .devcontainer/.env.template .devcontainer/.env
   ```

3. Edit `.devcontainer/.env` and replace `your_pat_token_here` with your actual PAT:
   ```
   AZURE_DEVOPS_PAT=your_actual_pat_token_here
   ```

⚠️ **Important**: Never commit the `.env` file to source control as it contains sensitive information.

## Opening the DevContainer

### In VS Code (Local)
1. Open the repository in VS Code
2. When prompted, click "Reopen in Container"
3. Or use Command Palette (Ctrl+Shift+P): "Remote-Containers: Reopen in Container"

### In GitHub Codespaces
1. Click the "Code" button on the GitHub repository
2. Select "Codespaces" tab
3. Click "Create codespace on main"

## What Happens During Setup

The DevContainer will automatically:

1. Install the .NET 9.0 SDK
2. Install Azure Artifacts Credential Provider
3. Configure NuGet authentication (if PAT is provided)
4. Restore NuGet packages
5. Set up VS Code extensions for C# development

## Troubleshooting

### Package Restoration Fails

If package restoration fails:

1. **Check your PAT**: Ensure it has "Packaging (read)" permissions
2. **Verify the PAT**: Test it manually with:
   ```bash
   curl -u :YOUR_PAT https://dev.azure.com/intelliTect/_apis/packaging/feeds
   ```
3. **Check environment**: Ensure `AZURE_DEVOPS_PAT` is set correctly

### DevContainer Won't Start

1. **Check Docker**: Ensure Docker Desktop is running
2. **Check VS Code Extensions**: Ensure Remote-Containers extension is installed
3. **Rebuild Container**: Use Command Palette: "Remote-Containers: Rebuild Container"

### No Access to Private Packages

If you don't have access to the private Azure DevOps feed:

1. The setup script will automatically set `AccessToNugetFeed=false`
2. Private packages will be excluded from the build
3. The project will still build and run with public packages only

## Available Commands

Once the DevContainer is running, you can use these commands:

```bash
# Restore packages
dotnet restore

# Build the solution
dotnet build

# Run tests
dotnet test

# Run the web application
dotnet run --project EssentialCSharp.Web

# Run the chat application
dotnet run --project EssentialCSharp.Chat
```