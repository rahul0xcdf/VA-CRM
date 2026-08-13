namespace MotorPumpSuite.Application.Companies;

/// <summary>
/// Read model for a company. Entities do not cross into the presentation layer.
/// </summary>
public sealed record CompanySummary(
    Guid Id,
    string Name,
    string LegalName,
    string? Gstin,
    string StateCode,
    int FinancialYearStartMonth,
    bool IsActive);
