using Microsoft.Extensions.DependencyInjection;

namespace VinayakaAgencies.Application.Tests.TestDoubles;

/// <summary>
/// Minimal <see cref="IServiceCollection"/> implementation backed by a plain list.
/// </summary>
/// <remarks>
/// <see cref="IServiceCollection"/> is nothing more than <c>IList&lt;ServiceDescriptor&gt;</c>, so a
/// <see cref="List{T}"/> satisfies it without pulling in the concrete
/// <c>Microsoft.Extensions.DependencyInjection</c> container package (which builds service
/// providers) — this unit only needs to inspect *registrations*, not resolve them, and the
/// Application project already carries the Abstractions package that defines this interface.
/// </remarks>
public sealed class FakeServiceCollection : List<ServiceDescriptor>, IServiceCollection;
