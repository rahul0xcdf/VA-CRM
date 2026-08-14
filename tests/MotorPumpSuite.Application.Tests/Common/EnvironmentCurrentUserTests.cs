using MotorPumpSuite.Application.Common;
using Shouldly;

namespace MotorPumpSuite.Application.Tests.Common;

/// <summary>
/// <see cref="EnvironmentCurrentUser"/> reads <see cref="Environment.MachineName"/> and
/// <see cref="Environment.UserName"/> directly with no injected seam, so — unlike
/// <see cref="CompanyService"/> — it cannot be driven through fabricated "machine name blank"
/// inputs from a test without adding a testability seam to production code, which is out of scope
/// for this unit (see the final report). What is exercised here is the branch that is actually
/// reachable on a real Windows test host (both values present) and, per the type's own contract,
/// the never-blank guarantee — the one <see cref="ICurrentUser"/>'s remarks call out as the reason
/// this type exists at all: an unattributable change is not an audit trail.
/// </summary>
public sealed class EnvironmentCurrentUserTests
{
    [Fact]
    public void UserName_MachineAndUserBothPresent_ReturnsMachineBackslashUser()
    {
        var machine = Environment.MachineName;
        var user = Environment.UserName;
        machine.ShouldNotBeNullOrWhiteSpace();
        user.ShouldNotBeNullOrWhiteSpace();

        var sut = new EnvironmentCurrentUser();

        sut.UserName.ShouldBe($@"{machine}\{user}");
    }

    [Fact]
    public void UserName_Always_IsNeverBlankOrWhitespace()
    {
        var sut = new EnvironmentCurrentUser();

        sut.UserName.ShouldNotBeNullOrWhiteSpace();
    }
}
