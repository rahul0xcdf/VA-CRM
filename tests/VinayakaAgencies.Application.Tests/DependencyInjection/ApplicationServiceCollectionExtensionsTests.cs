using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.DependencyInjection;
using VinayakaAgencies.Application.Tests.TestDoubles;
using VinayakaAgencies.Domain.Abstractions;
using Shouldly;

namespace VinayakaAgencies.Application.Tests.DependencyInjection;

/// <summary>
/// These tests inspect <see cref="ServiceDescriptor"/> registrations directly via
/// <see cref="FakeServiceCollection"/> rather than building a real
/// <c>ServiceProvider</c> — that concrete container type lives in a package this test project
/// deliberately does not reference (see <see cref="FakeServiceCollection"/>'s remarks). Registration
/// shape (service type, implementation type, lifetime) is exactly what the checkpoint asks for.
/// </summary>
public sealed class ApplicationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddApplication_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        Should.Throw<ArgumentNullException>(() => services.AddApplication());
    }

    [Fact]
    public void AddApplication_Always_RegistersClockAsSingleton()
    {
        var services = new FakeServiceCollection();

        services.AddApplication();

        var descriptor = services.Single(d => d.ServiceType == typeof(IClock));
        descriptor.ImplementationType.ShouldBe(typeof(SystemClock));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddApplication_Always_RegistersCurrentUserAsSingleton()
    {
        var services = new FakeServiceCollection();

        services.AddApplication();

        var descriptor = services.Single(d => d.ServiceType == typeof(ICurrentUser));
        descriptor.ImplementationType.ShouldBe(typeof(EnvironmentCurrentUser));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddApplication_Always_RegistersCompanyServiceAsScoped()
    {
        var services = new FakeServiceCollection();

        services.AddApplication();

        var descriptor = services.Single(d => d.ServiceType == typeof(ICompanyService));
        descriptor.ImplementationType.ShouldBe(typeof(CompanyService));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddApplication_ClockAlreadyRegistered_PreRegisteredInstanceWins()
    {
        // TryAddSingleton is how tests and later phases substitute a fixed clock; this asserts that
        // seam actually behaves as advertised rather than AddApplication clobbering it.
        var services = new FakeServiceCollection();
        var fixedClock = new FixedClock(DateTimeOffset.UtcNow, DateOnly.FromDateTime(DateTime.Today));
        services.AddSingleton<IClock>(fixedClock);

        services.AddApplication();

        var clockDescriptors = services.Where(d => d.ServiceType == typeof(IClock)).ToList();
        clockDescriptors.Count.ShouldBe(1);
        clockDescriptors[0].ImplementationInstance.ShouldBeSameAs(fixedClock);
    }

    [Fact]
    public void AddApplication_CurrentUserAlreadyRegistered_PreRegisteredInstanceWins()
    {
        var services = new FakeServiceCollection();
        var stubUser = new StubCurrentUser(@"TESTHOST\qa");
        services.AddSingleton<ICurrentUser>(stubUser);

        services.AddApplication();

        var userDescriptors = services.Where(d => d.ServiceType == typeof(ICurrentUser)).ToList();
        userDescriptors.Count.ShouldBe(1);
        userDescriptors[0].ImplementationInstance.ShouldBeSameAs(stubUser);
    }

    [Fact]
    public void AddApplication_Always_ReturnsTheSameServiceCollectionForChaining()
    {
        var services = new FakeServiceCollection();

        var result = services.AddApplication();

        result.ShouldBeSameAs(services);
    }
}
