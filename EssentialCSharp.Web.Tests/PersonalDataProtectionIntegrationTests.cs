using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Integration tests for Personal Data Protection functionality with Identity User
/// </summary>
public class PersonalDataProtectionIntegrationTests
{
    [Fact]
    public void PersonalDataProtectionService_ImplementsIPersonalDataProtector()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDataProtection();
        var serviceProvider = services.BuildServiceProvider();
        var dataProtectionProvider = serviceProvider.GetRequiredService<IDataProtectionProvider>();
        
        // Act
        var service = new PersonalDataProtectionService(dataProtectionProvider);

        // Assert
        Assert.IsAssignableFrom<IPersonalDataProtector>(service);
    }

    [Fact]
    public void EssentialCSharpWebUser_HasProtectedPersonalDataAttributes()
    {
        // Arrange & Act
        var user = new EssentialCSharpWebUser();
        var firstNameProperty = typeof(EssentialCSharpWebUser).GetProperty(nameof(EssentialCSharpWebUser.FirstName));
        var lastNameProperty = typeof(EssentialCSharpWebUser).GetProperty(nameof(EssentialCSharpWebUser.LastName));

        // Assert
        Assert.NotNull(firstNameProperty);
        Assert.NotNull(lastNameProperty);
        
        var firstNameAttributes = firstNameProperty.GetCustomAttributes(typeof(ProtectedPersonalDataAttribute), false);
        var lastNameAttributes = lastNameProperty.GetCustomAttributes(typeof(ProtectedPersonalDataAttribute), false);
        
        Assert.NotEmpty(firstNameAttributes);
        Assert.NotEmpty(lastNameAttributes);
    }

    [Fact]
    public void PersonalDataProtectionService_CanProtectUserPersonalData()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDataProtection();
        var serviceProvider = services.BuildServiceProvider();
        var dataProtectionProvider = serviceProvider.GetRequiredService<IDataProtectionProvider>();
        var service = new PersonalDataProtectionService(dataProtectionProvider);

        var testFirstName = "John";
        var testLastName = "Doe";

        // Act
        var protectedFirstName = service.Protect(testFirstName);
        var protectedLastName = service.Protect(testLastName);

        var unprotectedFirstName = service.Unprotect(protectedFirstName);
        var unprotectedLastName = service.Unprotect(protectedLastName);

        // Assert
        Assert.NotEqual(testFirstName, protectedFirstName);
        Assert.NotEqual(testLastName, protectedLastName);
        Assert.Equal(testFirstName, unprotectedFirstName);
        Assert.Equal(testLastName, unprotectedLastName);
    }
}