using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EssentialCSharp.Web.Tests;

public class PersonalDataProtectionServiceTests
{
    private PersonalDataProtectionService CreateService()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        var serviceProvider = services.BuildServiceProvider();
        var dataProtectionProvider = serviceProvider.GetRequiredService<IDataProtectionProvider>();
        
        return new PersonalDataProtectionService(dataProtectionProvider);
    }

    [Fact]
    public void Protect_WithValidData_ReturnsEncryptedString()
    {
        // Arrange
        var service = CreateService();
        var testData = "John Doe";

        // Act
        var protectedData = service.Protect(testData);

        // Assert
        Assert.NotNull(protectedData);
        Assert.NotEmpty(protectedData);
        Assert.NotEqual(testData, protectedData);
    }

    [Fact]
    public void Unprotect_WithProtectedData_ReturnsOriginalString()
    {
        // Arrange
        var service = CreateService();
        var testData = "Jane Smith";

        // Act
        var protectedData = service.Protect(testData);
        var unprotectedData = service.Unprotect(protectedData);

        // Assert
        Assert.Equal(testData, unprotectedData);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Protect_WithNullOrEmptyData_ReturnsEmptyString(string? testData)
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.Protect(testData);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Unprotect_WithNullOrEmptyData_ReturnsEmptyString(string? testData)
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.Unprotect(testData);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Unprotect_WithUnencryptedData_ReturnsOriginalDataForBackwardCompatibility()
    {
        // Arrange
        var service = CreateService();
        var unencryptedData = "This is plain text data";

        // Act
        var result = service.Unprotect(unencryptedData);

        // Assert
        // Should return the original data when decryption fails (backward compatibility)
        Assert.Equal(unencryptedData, result);
    }

    [Fact]
    public void ProtectAndUnprotect_WithSpecialCharacters_WorksCorrectly()
    {
        // Arrange
        var service = CreateService();
        var testData = "Special chars: éñüñëç@#$%^&*()";

        // Act
        var protectedData = service.Protect(testData);
        var unprotectedData = service.Unprotect(protectedData);

        // Assert
        Assert.Equal(testData, unprotectedData);
        Assert.NotEqual(testData, protectedData);
    }
}