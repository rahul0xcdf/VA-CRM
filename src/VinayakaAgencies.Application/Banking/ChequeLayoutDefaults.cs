using VinayakaAgencies.Application.Printing;

namespace VinayakaAgencies.Application.Banking;

/// <summary>
/// The field names a cheque layout may place, and one CTS-2010 starting layout
/// (phase-7-tds-payroll-banking.md §4a D4, checkpoint C7.8).
/// </summary>
/// <remarks>
/// <para>
/// <b>The measurements here are a starting point, not a specification.</b> CTS-2010 standardised
/// the leaf at 202 × 92 mm and fixed roughly where each box sits, which is why one adjustable
/// layout covers most banks. It does not make every bank identical: the owner prints onto a real
/// cheque, sees what is a few millimetres out, and edits these numbers. That edit appends a
/// template version like any other and nothing is rebuilt — which is exactly what C7.8 asks for.
/// </para>
/// <para>
/// No bank is named and no bank-specific layout is seeded (§4a D4). Where a bank genuinely differs,
/// it is another template row resolved by priority, the same shape as the two seeded tax-invoice
/// templates (C3.6).
/// </para>
/// </remarks>
public static class ChequeLayoutDefaults
{
    /// <summary>The template name provisioning creates.</summary>
    public const string DefaultTemplateName = "Standard cheque (CTS-2010)";

    /// <summary>The payee line — who the cheque is drawn in favour of.</summary>
    public const string PayeeField = "Payee";

    /// <summary>The date, printed into the DDMMYYYY boxes at the top right.</summary>
    public const string DateField = "Date";

    /// <summary>The amount in words, which is the legally operative one.</summary>
    public const string AmountInWordsField = "AmountInWords";

    /// <summary>The amount in figures, in the boxed panel at the right.</summary>
    public const string AmountInFiguresField = "AmountInFigures";

    /// <summary>The "A/C Payee" crossing, for banks whose leaves are not pre-crossed.</summary>
    public const string AccountPayeeField = "AccountPayee";

    /// <summary>Every field the renderer understands. A name outside this set prints nothing.</summary>
    public static IReadOnlyList<string> FieldNames { get; } =
    [
        PayeeField,
        DateField,
        AmountInWordsField,
        AmountInFiguresField,
        AccountPayeeField,
    ];

    /// <summary>
    /// A layout positioned for a standard 202 × 92 mm CTS-2010 leaf, for the owner to adjust.
    /// </summary>
    /// <remarks>
    /// The amount in words is given a wrapping width and a second line's worth of room, because a
    /// large amount genuinely runs to two lines on a cheque and a single-line field would clip it
    /// — silently, which on a cheque means the bank returns it.
    /// </remarks>
    public static PrintTemplateLayout Default { get; } = new(
        Title: "Cheque",
        ShowLogo: false,
        LogoPath: null,
        HeaderLines: [],
        FooterNote: null,
        TermsAndConditions: null,
        VisibleColumns: [],
        CopyLabels: [],
        Fields:
        [
            new PrintFieldPlacement(DateField, LeftMm: 150d, TopMm: 8d, FontSize: 12d, IsBold: true),
            new PrintFieldPlacement(PayeeField, LeftMm: 22d, TopMm: 25d, WidthMm: 120d, FontSize: 11d),
            new PrintFieldPlacement(
                AmountInWordsField,
                LeftMm: 30d,
                TopMm: 35d,
                WidthMm: 120d,
                FontSize: 10d),
            new PrintFieldPlacement(
                AmountInFiguresField,
                LeftMm: 155d,
                TopMm: 40d,
                WidthMm: 40d,
                FontSize: 12d,
                IsBold: true),
            new PrintFieldPlacement(
                AccountPayeeField,
                LeftMm: 8d,
                TopMm: 6d,
                FontSize: 9d,
                IsVisible: false),
        ]);
}
