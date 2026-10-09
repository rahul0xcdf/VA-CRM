using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Payroll;

/// <summary>
/// A person on the payroll (phase-7-tds-payroll-banking.md §4a D3).
/// </summary>
/// <remarks>
/// <para>
/// The statutory identifiers are all nullable, and that is not laxity. A PF account is allotted
/// after joining, an ESI number after registration, and a new joiner legitimately has neither on the
/// day their first salary is computed. Requiring them would make the software refuse a correct
/// entry; carrying them as nullable and surfacing the gap on the challan report puts the pressure
/// where it belongs.
/// </para>
/// <para>
/// <b>Leaving is a date, not a flag.</b> A leaver's salary for their final part-month still has to
/// compute, their PF still has to be remitted, and last year's pay slips still have to be
/// reproducible — none of which works if the employee row is deactivated and filtered out.
/// <see cref="IsOnRollOn"/> answers "were they employed on this date", which is the question every
/// payroll run actually asks.
/// </para>
/// </remarks>
public sealed class Employee : Entity, IAuditStamped
{
    /// <summary>A Universal Account Number is twelve digits.</summary>
    public const int UanLength = 12;

    private Employee()
    {
        // EF Core materialisation.
    }

    public Employee(
        Guid companyId,
        string code,
        string name,
        DateOnly joinedOn,
        string? designation = null,
        string? pan = null,
        string? uan = null,
        string? esiNumber = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        SetName(name);
        JoinedOn = joinedOn;
        Designation = Normalise(designation);
        SetPan(pan);
        SetUan(uan);
        SetEsiNumber(esiNumber);
        IsActive = true;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>The employee number the company uses on its own records.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>Comparison form of <see cref="Name"/>, for lookup ordering and search.</summary>
    public string NameNormalised { get; private set; } = string.Empty;

    public string? Designation { get; private set; }

    public DateOnly JoinedOn { get; private set; }

    /// <summary>Last day on the payroll, or null while still employed.</summary>
    public DateOnly? LeftOn { get; private set; }

    /// <summary>The employee's PAN, where one has been furnished.</summary>
    public string? Pan { get; private set; }

    /// <summary>Universal Account Number, allotted for provident fund.</summary>
    public string? Uan { get; private set; }

    /// <summary>The insurance number allotted under the ESI scheme.</summary>
    public string? EsiNumber { get; private set; }

    /// <summary>
    /// False for a row created in error. Distinct from <see cref="LeftOn"/>: a leaver is still an
    /// employee of record, whereas an inactive row was never a person on this payroll.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    /// <summary>Whether this person was on the payroll on the given date.</summary>
    public bool IsOnRollOn(DateOnly date) =>
        IsActive && date >= JoinedOn && (!LeftOn.HasValue || date <= LeftOn.Value);

    public void SetName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        NameNormalised = NameNormaliser.Normalise(name);
    }

    public void SetDesignation(string? designation) => Designation = Normalise(designation);

    /// <summary>
    /// Records the last day on the payroll.
    /// </summary>
    /// <remarks>
    /// Refused before the joining date, because a leaving date earlier than the joining date makes
    /// <see cref="IsOnRollOn"/> false for every day and the employee would silently vanish from
    /// every payroll run rather than producing an error anybody could act on.
    /// </remarks>
    public void RecordLeaving(DateOnly leftOn)
    {
        if (leftOn < JoinedOn)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leftOn), leftOn, "An employee cannot leave before they joined.");
        }

        LeftOn = leftOn;
    }

    /// <summary>Clears a leaving date recorded in error, or records a rejoining.</summary>
    public void ClearLeaving() => LeftOn = null;

    public void SetPan(string? pan)
    {
        if (string.IsNullOrWhiteSpace(pan))
        {
            Pan = null;
            return;
        }

        var trimmed = pan.Trim().ToUpperInvariant();

        if (trimmed.Length != Tds.TaxPayeeProfile.PanLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pan), pan, $"A PAN is exactly {Tds.TaxPayeeProfile.PanLength} characters.");
        }

        Pan = trimmed;
    }

    public void SetUan(string? uan)
    {
        if (string.IsNullOrWhiteSpace(uan))
        {
            Uan = null;
            return;
        }

        var trimmed = uan.Trim();

        if (trimmed.Length != UanLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uan), uan, $"A UAN is exactly {UanLength} digits.");
        }

        Uan = trimmed;
    }

    /// <summary>
    /// Records the ESI insurance number.
    /// </summary>
    /// <remarks>
    /// Length is bounded but the pattern is not checked, for the same reason
    /// <c>TaxPayeeProfile.SetPan</c> does not check PAN's: a number the software disagrees with is
    /// still the number the scheme allotted, and refusing it would block a real record.
    /// </remarks>
    public void SetEsiNumber(string? esiNumber) => EsiNumber = Normalise(esiNumber);

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
