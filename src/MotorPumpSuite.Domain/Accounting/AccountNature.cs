namespace MotorPumpSuite.Domain.Accounting;

/// <summary>
/// Which financial statement a group's balance lands on, and which way it naturally sits.
/// </summary>
/// <remarks>
/// Assets and expenses are naturally debit; liabilities and income naturally credit. This drives
/// the balance sheet / P&amp;L split and the sign presented in reports — it never changes how a
/// journal line is stored, which is always a signed amount (see <c>JournalLine</c>).
/// </remarks>
public enum AccountNature
{
    Asset = 1,
    Liability = 2,
    Income = 3,
    Expense = 4,
}
