using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.DependencyInjection;
using VinayakaAgencies.Application.Security;
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
    public void AddApplication_Always_RegistersUserSessionAsSingleton()
    {
        var services = new FakeServiceCollection();

        services.AddApplication();

        var descriptor = services.Single(d => d.ServiceType == typeof(IUserSession));
        descriptor.ImplementationType.ShouldBe(typeof(UserSession));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddApplication_Always_RegistersCurrentUserAsSingletonReadingThroughTheSession()
    {
        // Phase 9 §4a D2 changed this registration: the audit trail must name the signed-in
        // application account, not the Windows login a whole counter shares. So ICurrentUser is no
        // longer EnvironmentCurrentUser directly — it is a SessionCurrentUser wrapping it.
        var services = new FakeServiceCollection();

        services.AddApplication();

        var descriptor = services.Single(d => d.ServiceType == typeof(ICurrentUser));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        descriptor.ImplementationFactory.ShouldNotBeNull();

        var resolved = descriptor.ImplementationFactory!(new SessionOnlyProvider(new UserSession()));
        resolved.ShouldBeOfType<SessionCurrentUser>();
    }

    [Fact]
    public void AddApplication_NobodySignedIn_CurrentUserStillNamesSomebody()
    {
        // ICurrentUser's contract is that it never resolves to blank — an unattributable change is
        // not an audit trail (non-negotiable #3). The state worth pinning is the one Phase 9
        // introduced: the session is empty, because provisioning runs before any login prompt could
        // have been shown.
        var services = new FakeServiceCollection();
        services.AddApplication();
        var descriptor = services.Single(d => d.ServiceType == typeof(ICurrentUser));

        var resolved = (ICurrentUser)descriptor.ImplementationFactory!(
            new SessionOnlyProvider(new UserSession()));

        resolved.UserName.ShouldNotBeNullOrWhiteSpace();
        resolved.UserName.ShouldStartWith(SessionCurrentUser.UnauthenticatedPrefix);
    }

    /// <summary>
    /// The one dependency the <see cref="ICurrentUser"/> factory asks for.
    /// </summary>
    /// <remarks>
    /// This project does not reference the concrete container (see
    /// <see cref="FakeServiceCollection"/>), so the factory is invoked against this instead of a
    /// built provider. Anything other than <see cref="IUserSession"/> is a bug in the factory, so
    /// it throws rather than returning null.
    /// </remarks>
    private sealed class SessionOnlyProvider(IUserSession session) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IUserSession)
                ? session
                : throw new InvalidOperationException(
                    $"The ICurrentUser factory asked for {serviceType.Name}, which it should not need.");
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
