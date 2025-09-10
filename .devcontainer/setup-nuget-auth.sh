#!/bin/bash
set -e

echo "Setting up NuGet authentication for Azure DevOps..."

# Load environment variables from .env file if it exists
if [ -f ".devcontainer/.env" ]; then
    echo "Loading environment variables from .devcontainer/.env..."
    export $(grep -v '^#' .devcontainer/.env | xargs)
fi

# Install Azure Artifacts Credential Provider
echo "Installing Azure Artifacts Credential Provider..."
if ! sh -c "$(curl -fsSL https://aka.ms/install-artifacts-credprovider.sh)" 2>/dev/null; then
    echo "⚠️  Could not download Azure Artifacts Credential Provider installer."
    echo "This may be due to network restrictions. The provider will be installed later if needed."
fi

# Check if AZURE_DEVOPS_PAT is set
if [ -z "$AZURE_DEVOPS_PAT" ]; then
    echo ""
    echo "⚠️  AZURE_DEVOPS_PAT environment variable is not set."
    echo "To enable private NuGet feed access, you need to:"
    echo "1. Create a Personal Access Token (PAT) in Azure DevOps with 'Packaging (read)' permissions"
    echo "2. Add it to your devcontainer environment by creating a .devcontainer/.env file:"
    echo "   AZURE_DEVOPS_PAT=your_pat_token_here"
    echo "3. Or set it as a codespace secret named 'AZURE_DEVOPS_PAT'"
    echo ""
    echo "For now, setting AccessToNugetFeed=false to allow restore without private packages..."
    export ACCESS_TO_NUGET_FEED=false
else
    echo "AZURE_DEVOPS_PAT found, setting up authentication..."
    # Set up the credential provider environment
    export VSS_NUGET_EXTERNAL_FEED_ENDPOINTS="{\"endpointCredentials\": [{\"endpoint\":\"https://pkgs.dev.azure.com/intelliTect/_packaging/EssentialCSharp/nuget/v3/index.json\", \"password\":\"$AZURE_DEVOPS_PAT\"}]}"
    export ACCESS_TO_NUGET_FEED=true
    echo "✅ NuGet authentication configured for Azure DevOps private feed"
fi

# Display .NET version
echo "Checking .NET SDK version..."
dotnet --version

# Try to restore packages
echo "Attempting to restore NuGet packages..."
if dotnet restore -p:AccessToNugetFeed=$ACCESS_TO_NUGET_FEED; then
    echo "✅ Package restoration successful!"
else
    echo "❌ Package restoration failed. Check your Azure DevOps PAT if you need private packages."
    exit 1
fi

echo "🎉 Devcontainer setup complete!"