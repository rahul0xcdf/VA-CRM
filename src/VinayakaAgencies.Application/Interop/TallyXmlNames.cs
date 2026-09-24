namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// The element and attribute names of Tally's XML interchange format.
/// </summary>
/// <remarks>
/// Shared by <see cref="TallyXmlWriter"/> and <see cref="TallyXmlReader"/> so the two cannot drift.
/// A round-trip test passing because both sides independently agreed on a misspelling would prove
/// nothing about whether Tally can read the file (C8.1) — naming them once makes that failure mode
/// a single wrong constant rather than two matching ones.
/// </remarks>
internal static class TallyXmlNames
{
    internal const string Envelope = "ENVELOPE";
    internal const string Header = "HEADER";
    internal const string TallyRequest = "TALLYREQUEST";
    internal const string Body = "BODY";
    internal const string ImportData = "IMPORTDATA";
    internal const string RequestDesc = "REQUESTDESC";
    internal const string ReportName = "REPORTNAME";
    internal const string StaticVariables = "STATICVARIABLES";
    internal const string CurrentCompany = "SVCURRENTCOMPANY";
    internal const string FromDate = "SVFROMDATE";
    internal const string ToDate = "SVTODATE";
    internal const string RequestData = "REQUESTDATA";
    internal const string TallyMessage = "TALLYMESSAGE";

    internal const string ActionAttribute = "ACTION";
    internal const string NameAttribute = "NAME";

    internal const string Name = "NAME";
    internal const string Guid = "GUID";
    internal const string Parent = "PARENT";
    internal const string Description = "DESCRIPTION";
    internal const string Alias = "ALIAS";
    internal const string IsActive = "ISACTIVE";

    internal const string Group = "GROUP";
    internal const string NatureOfGroup = "NATUREOFGROUP";
    internal const string AffectsGrossProfit = "AFFECTSGROSSPROFIT";

    internal const string Ledger = "LEDGER";
    internal const string PartyGstin = "PARTYGSTIN";

    internal const string Unit = "UNIT";
    internal const string IsSimpleUnit = "ISSIMPLEUNIT";
    internal const string DecimalPlaces = "DECIMALPLACES";

    internal const string StockGroup = "STOCKGROUP";
    internal const string Godown = "GODOWN";
    internal const string Address = "ADDRESS";

    internal const string StockItem = "STOCKITEM";
    internal const string BaseUnits = "BASEUNITS";
    internal const string PartNumber = "PARTNO";
    internal const string HsnCode = "HSNCODE";

    internal const string Voucher = "VOUCHER";
    internal const string VoucherTypeAttribute = "VCHTYPE";
    internal const string VoucherTypeName = "VOUCHERTYPENAME";
    internal const string VoucherNumber = "VOUCHERNUMBER";
    internal const string Date = "DATE";
    internal const string EffectiveDate = "EFFECTIVEDATE";
    internal const string Narration = "NARRATION";
    internal const string Reference = "REFERENCE";
    internal const string ReferenceDate = "REFERENCEDATE";
    internal const string IsCancelled = "ISCANCELLED";
    internal const string PersistedView = "PERSISTEDVIEW";

    internal const string LedgerEntries = "ALLLEDGERENTRIES.LIST";
    internal const string LedgerName = "LEDGERNAME";
    internal const string IsDeemedPositive = "ISDEEMEDPOSITIVE";
    internal const string Amount = "AMOUNT";
    internal const string CostCentreAllocations = "CATEGORYALLOCATIONS.LIST";
    internal const string CostCentreList = "COSTCENTREALLOCATIONS.LIST";

    internal const string InventoryEntries = "ALLINVENTORYENTRIES.LIST";
    internal const string StockItemName = "STOCKITEMNAME";
    internal const string ActualQuantity = "ACTUALQTY";
    internal const string BilledQuantity = "BILLEDQTY";
    internal const string Rate = "RATE";
    internal const string BatchAllocations = "BATCHALLOCATIONS.LIST";
    internal const string GodownName = "GODOWNNAME";

    /// <summary>
    /// The view a voucher is written in.
    /// </summary>
    /// <remarks>
    /// Tally reads a voucher differently depending on the view it declares. Accounting Voucher View
    /// is right for a voucher carrying only ledger entries; a voucher that also moves stock is
    /// written in the invoice view so the inventory entries are read rather than ignored.
    /// </remarks>
    internal const string AccountingView = "Accounting Voucher View";

    internal const string InvoiceView = "Invoice Voucher View";
}
