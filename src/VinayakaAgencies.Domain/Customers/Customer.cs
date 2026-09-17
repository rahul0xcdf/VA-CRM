using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Customers;

/// <summary>
/// A customer the business sells to: contact and GST details, billing / shipping addresses, place
/// of supply, credit terms and the category that prices them (phase-4-customer-pricing.md §2).
/// </summary>
/// <remarks>
/// The customer links one-to-one with an ordinary ledger under "Sundry Debtors"
/// (<see cref="LedgerId"/>) rather than replacing it: Phase 3's trade documents, statements and
/// every downstream report keep posting against ledgers, so nothing downstream changes shape.
/// Deleting a customer is refused by the database while their ledger or any price line stands —
/// customers are deactivated, as books require.
/// </remarks>
public sealed class Customer : Entity, IAuditStamped
{
    private Customer()
    {
        // EF Core materialisation.
    }

    public Customer(
        Guid companyId,
        string name,
        Guid ledgerId,
        Guid customerCategoryId,
        string? contactPerson = null,
        string? phoneNumber = null,
        string? email = null,
        string? gstin = null,
        string? placeOfSupplyStateCode = null,
        string? billingAddressLines = null,
        string? shippingAddressLines = null,
        decimal creditLimit = 0m,
        int? creditPeriodDays = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException(
                "A customer must link to its Sundry Debtors ledger.", nameof(ledgerId));
        }

        if (customerCategoryId == Guid.Empty)
        {
            throw new ArgumentException(
                "A customer must belong to exactly one category.", nameof(customerCategoryId));
        }

        CompanyId = companyId;
        SetName(name);
        LedgerId = ledgerId;
        CustomerCategoryId = customerCategoryId;
        SetContact(contactPerson, phoneNumber, email);
        SetGstin(gstin);
        SetPlaceOfSupplyStateCode(placeOfSupplyStateCode);
        SetAddresses(billingAddressLines, shippingAddressLines);
        SetCreditTerms(creditLimit, creditPeriodDays);
    }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Comparison form of <see cref="Name"/> over which lookup indexes are built. See
    /// <see cref="NameNormaliser"/>.
    /// </summary>
    public string NameNormalised { get; private set; } = string.Empty;

    /// <summary>
    /// The customer's account in the books — a ledger under "Sundry Debtors", created alongside the
    /// customer and unique per customer. Invoices post against it exactly as before Phase 4.
    /// </summary>
    public Guid LedgerId { get; private set; }

    /// <summary>The single category whose price list auto-applies for this customer.</summary>
    public Guid CustomerCategoryId { get; private set; }

    public string? ContactPerson { get; private set; }

    public string? PhoneNumber { get; private set; }

    public string? Email { get; private set; }

    /// <summary>
    /// GSTIN of the customer's registration, when registered. Nullable: most retail customers are
    /// unregistered B2C sales.
    /// </summary>
    public string? Gstin { get; private set; }

    /// <summary>
    /// GST state code of the place of supply ("29" for Karnataka), which drives the CGST/SGST vs
    /// IGST split in Phase 5. Falls back to the company's own state when null.
    /// </summary>
    public string? PlaceOfSupplyStateCode { get; private set; }

    /// <summary>The billed-to address, newline-separated lines, printed as entered.</summary>
    public string? BillingAddressLines { get; private set; }

    /// <summary>The shipped-to address; null means "same as billing".</summary>
    public string? ShippingAddressLines { get; private set; }

    /// <summary>
    /// City or town — the IRP's <c>Loc</c> field for the buyer block
    /// (phase-6-einvoice-eway.md §4b). Separate from <see cref="BillingAddressLines"/> because a
    /// pincode and a city cannot be recovered from free text, and the portal needs both on their own.
    /// </summary>
    public string? City { get; private set; }

    /// <summary>Six-digit pincode — the IRP's <c>Pin</c> field for the buyer block.</summary>
    public int? Pincode { get; private set; }

    /// <summary>Credit limit — computed from minor units, never a column.</summary>
    public decimal CreditLimit => MoneyScale.FromMinor(CreditLimitMinor);

    /// <summary>
    /// Credit limit, in minor units of 1/10,000 (ADR-0003). Zero means no limit.
    /// </summary>
    public long CreditLimitMinor { get; private set; }

    /// <summary>Days of credit allowed before an invoice falls overdue; null means no agreed period.</summary>
    public int? CreditPeriodDays { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        SetName(name);
    }

    /// <summary>Moves the customer to another category; their next new sale line reprices from it.</summary>
    public void MoveToCategory(Guid customerCategoryId)
    {
        if (customerCategoryId == Guid.Empty)
        {
            throw new ArgumentException(
                "A customer must belong to exactly one category.", nameof(customerCategoryId));
        }

        CustomerCategoryId = customerCategoryId;
    }

    public void SetContact(string? contactPerson, string? phoneNumber, string? email)
    {
        ContactPerson = string.IsNullOrWhiteSpace(contactPerson) ? null : contactPerson.Trim();
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    public void SetGstin(string? gstin) =>
        Gstin = string.IsNullOrWhiteSpace(gstin) ? null : gstin.Trim().ToUpperInvariant();

    /// <summary>
    /// Sets the two-character GST state code of the place of supply; null falls back to the
    /// company's own state at document time.
    /// </summary>
    public void SetPlaceOfSupplyStateCode(string? placeOfSupplyStateCode)
    {
        if (string.IsNullOrWhiteSpace(placeOfSupplyStateCode))
        {
            PlaceOfSupplyStateCode = null;
            return;
        }

        var trimmed = placeOfSupplyStateCode.Trim();

        if (trimmed.Length != 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(placeOfSupplyStateCode),
                placeOfSupplyStateCode,
                "A place-of-supply state code is exactly two characters (e.g. \"29\").");
        }

        PlaceOfSupplyStateCode = trimmed;
    }

    public void SetAddresses(string? billingAddressLines, string? shippingAddressLines)
    {
        BillingAddressLines =
            string.IsNullOrWhiteSpace(billingAddressLines) ? null : billingAddressLines.Trim();
        ShippingAddressLines =
            string.IsNullOrWhiteSpace(shippingAddressLines) ? null : shippingAddressLines.Trim();
    }

    /// <summary>
    /// Sets the structured city and pincode the IRP needs. Kept apart from
    /// <see cref="SetAddresses"/> so that editing a printed address block cannot silently blank
    /// the fields an e-invoice depends on.
    /// </summary>
    public void SetCityAndPincode(string? city, int? pincode)
    {
        if (pincode is { } value and (< 100_000 or > 999_999))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pincode), value, "An Indian pincode has six digits.");
        }

        City = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        Pincode = pincode;
    }

    /// <summary>
    /// Sets the credit terms. A zero limit disables the check entirely; a negative one has no
    /// meaning, so it is rejected rather than silently treated as unlimited.
    /// </summary>
    public void SetCreditTerms(decimal creditLimit, int? creditPeriodDays)
    {
        if (decimal.IsNegative(creditLimit))
        {
            throw new ArgumentOutOfRangeException(
                nameof(creditLimit), creditLimit, "A credit limit cannot be negative.");
        }

        var rounded = MoneyScale.Round(creditLimit);

        if (rounded != creditLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(creditLimit),
                creditLimit,
                $"A credit limit carries more than {MoneyScale.Places} decimal places.");
        }

        if (creditPeriodDays < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(creditPeriodDays),
                creditPeriodDays,
                "A credit period cannot be negative. Zero means due on receipt; null means none agreed.");
        }

        CreditLimitMinor = MoneyScale.ToMinor(rounded);
        CreditPeriodDays = creditPeriodDays;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    /// <summary>Keeps <see cref="NameNormalised"/> in step with <see cref="Name"/>; the two are only
    /// ever written together.</summary>
    private void SetName(string name)
    {
        Name = name.Trim();
        NameNormalised = NameNormaliser.Normalise(name);
    }
}
