namespace MotorPumpSuite.Application.Common;

/// <summary>
/// The company whose books the current operation applies to.
/// </summary>
/// <remarks>
/// Every master and voucher carries a <c>CompanyId</c> (spec §10) even though only one company
/// exists at launch, because retrofitting that column later would touch every table. This interface
/// is how a service learns which one it is working in, so no service has to be handed a company id
/// through every method signature.
/// </remarks>
public interface ICompanyContext
{
    /// <summary>The active company. Throws if no company has been selected or created yet.</summary>
    Guid CompanyId { get; }

    bool HasCompany { get; }

    /// <summary>Resolves the active company, creating the default one on a fresh install.</summary>
    Task<Guid> EnsureCompanyAsync(CancellationToken cancellationToken = default);
}
