using System.Reflection;

namespace RoboCamHub.Domain.Tests;

public sealed class DomainDependencyTests
{
    [Fact]
    public void DomainAssemblyDoesNotReferenceAvalonia()
    {
        var domainAssembly = Assembly.Load("RoboCamHub.Domain");

        Assert.DoesNotContain(
            domainAssembly.GetReferencedAssemblies(),
            reference => reference.Name?.StartsWith("Avalonia", StringComparison.Ordinal) is true);
    }
}
