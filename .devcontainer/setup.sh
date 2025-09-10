#!/bin/bash

echo "🔧 Setting up EssentialCSharp.Web development environment..."

# Verify .NET SDK version
echo "📋 Checking .NET SDK version..."
dotnet --version

# List available SDKs
echo "📋 Available .NET SDKs:"
dotnet --list-sdks

# Check if we have the Azure DevOps PAT token
if [ -z "$NUGET_AUTH_TOKEN" ]; then
    echo "⚠️  WARNING: AZURE_DEVOPS_PAT environment variable is not set!"
    echo "   Private NuGet packages from Azure DevOps will not be accessible."
    echo "   Please set the AZURE_DEVOPS_PAT environment variable with your Azure DevOps Personal Access Token."
    echo "   See README.md for setup instructions."
    echo ""
    echo "🔧 Restoring packages without private feed access..."
    dotnet restore -p:AccessToNugetFeed=false
else
    echo "✅ Azure DevOps PAT token found - configuring private NuGet feed..."
    
    # Remove existing source if it exists, then add with authentication
    dotnet nuget remove source "EssentialCSharp" --configfile nuget.config 2>/dev/null || true
    
    # Configure Azure DevOps NuGet source with authentication
    dotnet nuget add source "https://pkgs.dev.azure.com/intelliTect/_packaging/EssentialCSharp/nuget/v3/index.json" \
        --name "EssentialCSharp" \
        --username "devcontainer" \
        --password "$NUGET_AUTH_TOKEN" \
        --store-password-in-clear-text \
        --configfile nuget.config
    
    echo "🔧 Restoring packages with private feed access..."
    if dotnet restore -p:AccessToNugetFeed=true; then
        echo "✅ Package restore successful with private feed access!"
    else
        echo "⚠️  Package restore failed with private feed access."
        echo "   This might indicate an authentication issue with the Azure DevOps PAT token."
        echo "   Falling back to public packages only..."
        dotnet restore -p:AccessToNugetFeed=false
    fi
fi

echo "✅ Setup complete!"
echo ""
echo "🚀 Ready to develop! You can now:"
echo "   - Build: dotnet build"
echo "   - Test: dotnet test"
echo "   - Run web app: dotnet run --project EssentialCSharp.Web"
echo "   - Run chat app: dotnet run --project EssentialCSharp.Chat"