using System.Xml.Linq;
using MotorPumpSuite.Domain.Common;
using MotorPumpSuite.Domain.Tests.TestSupport;

namespace MotorPumpSuite.Domain.Tests.Guards;

/// <summary>
/// C0.6 — "Layering holds". CLAUDE.md's dependency rule ("Domain ← Application ← Infrastructure ←
/// App. Domain references nothing. Application never references EF Core.") is the whole reason the
/// Phase 9 SQLite → SQL Server migration is expected to be a configuration change and not a rewrite.
/// These tests fail the build the moment anyone reaches for EF Core, Dapper, or another project
/// reference from inside Domain or reaches for EF Core from Application.
/// </summary>
public class LayeringGuardTests
{
    [Fact]
    public void DomainCsproj_OnDisk_HasNoProjectReferences()
    {
        var csprojPath = Path.Combine(
            RepoRoot.Find(), "src", "MotorPumpSuite.Domain", "MotorPumpSuite.Domain.csproj");

        var document = XDocument.Load(csprojPath);
        var projectReferences = document.Descendants("ProjectReference").ToList();

        projectReferences.ShouldBeEmpty(
            "MotorPumpSuite.Domain must reference nothing (project.md dependency rule); "
            + $"found: {string.Join(", ", projectReferences.Select(e => e.Attribute("Include")?.Value))}");
    }

    [Fact]
    public void DomainCsproj_OnDisk_HasNoPackageReferences()
    {
        var csprojPath = Path.Combine(
            RepoRoot.Find(), "src", "MotorPumpSuite.Domain", "MotorPumpSuite.Domain.csproj");

        var document = XDocument.Load(csprojPath);
        var packageReferences = document.Descendants("PackageReference").ToList();

        packageReferences.ShouldBeEmpty(
            "MotorPumpSuite.Domain must have zero external package dependencies (entities, "
            + "value objects and abstractions only); "
            + $"found: {string.Join(", ", packageReferences.Select(e => e.Attribute("Include")?.Value))}");
    }

    [Fact]
    public void ApplicationCsproj_OnDisk_HasNoEntityFrameworkCorePackageReference()
    {
        var csprojPath = Path.Combine(
            RepoRoot.Find(), "src", "MotorPumpSuite.Application", "MotorPumpSuite.Application.csproj");

        var document = XDocument.Load(csprojPath);
        var efCoreReferences = document
            .Descendants("PackageReference")
            .Where(e => (e.Attribute("Include")?.Value ?? string.Empty)
                .StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .ToList();

        efCoreReferences.ShouldBeEmpty(
            "MotorPumpSuite.Application must not reference EF Core — only Infrastructure is "
            + "allowed to know the database exists; "
            + $"found: {string.Join(", ", efCoreReferences.Select(e => e.Attribute("Include")?.Value))}");
    }

    [Fact]
    public void DomainAssembly_AtRuntime_ReferencesNoEntityFrameworkCoreAssembly()
    {
        var domainAssembly = typeof(Entity).Assembly;

        var efCoreReferences = domainAssembly.GetReferencedAssemblies()
            .Where(a => (a.Name ?? string.Empty)
                .StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .ToList();

        efCoreReferences.ShouldBeEmpty(
            "the compiled MotorPumpSuite.Domain.dll must not carry an EF Core assembly "
            + $"reference; found: {string.Join(", ", efCoreReferences.Select(a => a.Name))}");
    }

    [Fact]
    public void DomainAssembly_AtRuntime_ReferencesNoOtherMotorPumpSuiteAssembly()
    {
        var domainAssembly = typeof(Entity).Assembly;

        var otherProjectReferences = domainAssembly.GetReferencedAssemblies()
            .Where(a => (a.Name ?? string.Empty).StartsWith("MotorPumpSuite.", StringComparison.Ordinal)
                && a.Name != domainAssembly.GetName().Name)
            .ToList();

        otherProjectReferences.ShouldBeEmpty(
            "Domain is the innermost layer and must not reference Application, Infrastructure or "
            + $"App; found: {string.Join(", ", otherProjectReferences.Select(a => a.Name))}");
    }

    [Fact]
    public void DomainAssembly_Name_IsMotorPumpSuiteDomain()
    {
        // Sanity check on the previous two tests: proves typeof(Entity).Assembly really is the
        // Domain assembly and not, say, this test assembly picking up an in-memory type.
        var domainAssembly = typeof(Entity).Assembly;

        domainAssembly.GetName().Name.ShouldBe("MotorPumpSuite.Domain");
    }
}
