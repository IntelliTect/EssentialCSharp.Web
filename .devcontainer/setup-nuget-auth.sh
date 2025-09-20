#!/bin/bash
set -e

echo "Setting up NuGet authentication for Azure DevOps..."

# Install Azure Artifacts Credential Provider
echo "Installing Azure Artifacts Credential Provider..."
if sh -c "$(curl -fsSL https://aka.ms/install-artifacts-credprovider.sh)" 2>/dev/null; then
    echo "✅ Azure Artifacts Credential Provider installed successfully"
else
    echo "⚠️  Could not download Azure Artifacts Credential Provider installer."
    echo "This may be due to network restrictions. Falling back to public packages only."
    export ACCESS_TO_NUGET_FEED=false
fi

# Display .NET version
echo "Checking .NET SDK version..."
dotnet --version

# Try to restore packages with interactive authentication if credential provider is available
echo "Attempting to restore NuGet packages..."
if command -v dotnet-credential-provider-installer >/dev/null 2>&1 || [ -n "$(find ~/.nuget -name "*CredentialProvider*" 2>/dev/null | head -1)" ]; then
    echo ""
    echo "🔐 The credential provider is available for Azure DevOps authentication."
    echo "If prompted for credentials during package restoration, you can:"
    echo "  1. Use your Azure DevOps account credentials, or"
    echo "  2. Create a Personal Access Token (PAT) with 'Packaging (read)' permissions"
    echo "     from: https://dev.azure.com/intelliTect/_usersSettings/tokens"
    echo ""
    
    # First try to restore with interactive authentication for private packages
    if dotnet restore --interactive -p:AccessToNugetFeed=true; then
        echo "✅ Package restoration successful with private feed access!"
    else
        echo "⚠️  Private package restoration failed or was cancelled."
        echo "Falling back to public packages only..."
        if dotnet restore -p:AccessToNugetFeed=false; then
            echo "✅ Package restoration successful with public packages only!"
        else
            echo "❌ Package restoration failed completely."
            exit 1
        fi
    fi
else
    echo "Credential provider not available, using public packages only..."
    if dotnet restore -p:AccessToNugetFeed=false; then
        echo "✅ Package restoration successful with public packages only!"
    else
        echo "❌ Package restoration failed."
        exit 1
    fi
fi

echo "🎉 Devcontainer setup complete!"