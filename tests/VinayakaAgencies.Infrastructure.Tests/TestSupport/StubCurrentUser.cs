using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Infrastructure.Tests.TestSupport;

/// <summary>
/// A settable current user, so attribution can be asserted and a blank name can be forced.
/// </summary>
public sealed class StubCurrentUser : ICurrentUser
{
    public StubCurrentUser(string userName = @"TESTHOST\tester")
    {
        UserName = userName;
    }

    public string UserName { get; set; }
}
