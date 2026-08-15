using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Application.Tests.TestDoubles;

/// <summary>A fixed identity for tests that need attribution without a real Windows session.</summary>
public sealed class StubCurrentUser(string userName) : ICurrentUser
{
    public string UserName { get; } = userName;
}
