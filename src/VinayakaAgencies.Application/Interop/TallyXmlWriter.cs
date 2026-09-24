using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// Writes an <see cref="InteropDocument"/> as Tally's XML "Data Interchange" envelope.
/// </summary>
/// <remarks>
/// <para>
/// Masters are written before vouchers, and in dependency order within the masters — units, stock
/// groups and godowns before the stock items that name them; account groups before their ledgers.
/// Tally reads the messages in document order, so this is what lets a single file create a master
/// and then post against it.
/// </para>
/// <para>
/// Nothing here decides an encoding. Dates, amounts, signs, quantities and voucher type names all
/// come from <see cref="TallyConventions"/>, so the assumptions that a real Tally import will test
/// (open-questions.md #22) live in one file rather than being spread across this one and the reader.
/// </para>
/// </remarks>
public static class TallyXmlWriter
{
    private static readonly XmlWriterSettings Settings = new()
    {
        Indent = true,
        IndentChars = "  ",
        OmitXmlDeclaration = false,

        // Tally's importer is written against ASCII-era files and is unreliable with a byte-order
        // mark. Writing without one costs nothing and removes a failure that presents as "the file
        // is empty" rather than as an encoding problem.
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
    };

    /// <summary>Renders the document to a string.</summary>
    /// <remarks>
    /// Rendered through a byte stream rather than a <see cref="StringBuilder"/>, so the text this
    /// returns is exactly what <see cref="WriteAsync"/> would put in the file. Writing to a
    /// <c>StringBuilder</c> makes <c>XmlWriter</c> declare <c>encoding="utf-16"</c> regardless of
    /// the settings, and a file saved from that string would carry a declaration contradicting its
    /// own bytes.
    /// </remarks>
    public static string Write(InteropDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var stream = new MemoryStream();

        using (var writer = XmlWriter.Create(stream, Settings))
        {
            BuildEnvelope(document).WriteTo(writer);
        }

        return Settings.Encoding.GetString(stream.ToArray());
    }

    /// <summary>Renders the document straight to a stream, without staging it as a string first.</summary>
    /// <remarks>
    /// The element tree is still built in memory, so this is not streaming in the sense §4a D2 asks
    /// of the <i>importer</i> — it only avoids holding the rendered text and the tree at the same
    /// time. Export volume is bounded by what the books hold; an import file is bounded by what
    /// someone else sends us, which is why only one of the two is written to stream properly.
    /// </remarks>
    public static async Task WriteAsync(
        InteropDocument document,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);

        var settings = Settings.Clone();
        settings.Async = true;

        var writer = XmlWriter.Create(destination, settings);

        await using (writer.ConfigureAwait(false))
        {
            await BuildEnvelope(document).WriteToAsync(writer, cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
    }

    internal static XElement BuildEnvelope(InteropDocument document)
    {
        var staticVariables = new XElement(
            TallyXmlNames.StaticVariables,
            new XElement(TallyXmlNames.CurrentCompany, document.CompanyName));

        if (document.FromDate is { } from)
        {
            staticVariables.Add(new XElement(TallyXmlNames.FromDate, TallyConventions.Date(from)));
        }

        if (document.ToDate is { } to)
        {
            staticVariables.Add(new XElement(TallyXmlNames.ToDate, TallyConventions.Date(to)));
        }

        return new XElement(
            TallyXmlNames.Envelope,
            new XElement(
                TallyXmlNames.Header,
                new XElement(TallyXmlNames.TallyRequest, TallyConventions.ImportRequest)),
            new XElement(
                TallyXmlNames.Body,
                new XElement(
                    TallyXmlNames.ImportData,
                    new XElement(
                        TallyXmlNames.RequestDesc,
                        new XElement(TallyXmlNames.ReportName, TallyConventions.ReportNameFor(document)),
                        staticVariables),
                    new XElement(TallyXmlNames.RequestData, Messages(document)))));
    }

    private static IEnumerable<XElement> Messages(InteropDocument document)
    {
        foreach (var unit in document.Units)
        {
            yield return Message(Unit(unit));
        }

        foreach (var group in document.StockGroups)
        {
            yield return Message(StockGroup(group));
        }

        foreach (var godown in document.Godowns)
        {
            yield return Message(Godown(godown));
        }

        foreach (var item in document.Items)
        {
            yield return Message(StockItem(item));
        }

        // Account groups in tree order: a child naming a parent Tally has not read yet is the one
        // ordering mistake that produces a partially imported chart of accounts rather than an error.
        foreach (var group in InteropOrdering.ParentsFirst(document.AccountGroups))
        {
            yield return Message(AccountGroup(group));
        }

        foreach (var ledger in document.Ledgers)
        {
            yield return Message(Ledger(ledger));
        }

        // Voucher types before the vouchers that name them: a voucher filed under a type the reader
        // has not met yet has no behaviour it can be posted with.
        foreach (var voucherType in document.VoucherTypes)
        {
            yield return Message(VoucherType(voucherType));
        }

        foreach (var voucher in document.Vouchers)
        {
            yield return Message(Voucher(voucher));
        }
    }

    private static XElement Message(XElement content) =>
        new(TallyXmlNames.TallyMessage, content);

    private static XElement Unit(InteropUnit unit)
    {
        var element = Master(TallyXmlNames.Unit, unit.Code, unit.Id);
        element.Add(new XElement(TallyXmlNames.IsSimpleUnit, TallyConventions.Bool(true)));
        element.Add(new XElement(
            TallyXmlNames.DecimalPlaces,
            unit.DecimalPlaces.ToString(CultureInfo.InvariantCulture)));
        AddOptional(element, TallyXmlNames.Description, unit.Description);
        AddActive(element, unit.IsActive);
        return element;
    }

    private static XElement StockGroup(InteropStockGroup group)
    {
        var element = Master(TallyXmlNames.StockGroup, group.Name, group.Id);
        AddOptional(element, TallyXmlNames.Description, group.Description);
        AddActive(element, group.IsActive);
        return element;
    }

    private static XElement Godown(InteropGodown godown)
    {
        var element = Master(TallyXmlNames.Godown, godown.Name, godown.Id);
        AddOptional(element, TallyXmlNames.Address, godown.Address);
        AddActive(element, godown.IsActive);
        return element;
    }

    private static XElement StockItem(InteropItem item)
    {
        var element = Master(TallyXmlNames.StockItem, item.Name, item.Id);
        element.Add(new XElement(TallyXmlNames.Parent, item.StockGroupName ?? string.Empty));
        element.Add(new XElement(TallyXmlNames.BaseUnits, item.BaseUnitCode));
        AddOptional(element, TallyXmlNames.PartNumber, item.Code);
        AddOptional(element, TallyXmlNames.HsnCode, item.HsnSacCode);
        AddActive(element, item.IsActive);
        return element;
    }

    private static XElement AccountGroup(InteropAccountGroup group)
    {
        var element = Master(TallyXmlNames.Group, group.Name, group.Id);
        element.Add(new XElement(TallyXmlNames.Parent, group.ParentName ?? TallyConventions.PrimaryParent));

        // Tally derives a group's nature from its parent, so this element is redundant there and may
        // well be ignored on import. It is written anyway because this application's own importer
        // needs it: a group whose parent is not in the same file has no other way to say which side
        // of the accounts it lands on, and C8.2 round-trips through this file.
        element.Add(new XElement(TallyXmlNames.NatureOfGroup, group.Nature.ToString()));
        element.Add(new XElement(
            TallyXmlNames.AffectsGrossProfit,
            TallyConventions.Bool(group.AffectsGrossProfit)));
        AddActive(element, group.IsActive);
        return element;
    }

    private static XElement Ledger(InteropLedger ledger)
    {
        var element = Master(TallyXmlNames.Ledger, ledger.Name, ledger.Id);
        element.Add(new XElement(TallyXmlNames.Parent, ledger.ParentGroupName));
        AddOptional(element, TallyXmlNames.Alias, ledger.Alias);
        AddOptional(element, TallyXmlNames.Description, ledger.Description);
        AddOptional(element, TallyXmlNames.PartyGstin, ledger.Gstin);
        AddActive(element, ledger.IsActive);
        return element;
    }

    private static XElement VoucherType(InteropVoucherType voucherType)
    {
        var element = Master(TallyXmlNames.VoucherTypeElement, voucherType.Name, voucherType.Id);

        // PARENT names the built-in type whose behaviour this one inherits. That is how Tally itself
        // says what a user-defined voucher type does, and it is what the reader reads the kind from.
        element.Add(new XElement(TallyXmlNames.Parent, TallyConventions.VoucherTypeName(voucherType.Kind)));
        element.Add(new XElement(TallyXmlNames.NumberingMethod, "Automatic"));

        if (!string.IsNullOrWhiteSpace(voucherType.NumberPrefix))
        {
            element.Add(new XElement(
                TallyXmlNames.PrefixDetails,
                new XElement(TallyXmlNames.Prefix, voucherType.NumberPrefix)));
        }

        AddActive(element, voucherType.IsActive);
        return element;
    }

    private static XElement Voucher(InteropVoucher voucher)
    {
        var element = new XElement(
            TallyXmlNames.Voucher,
            new XAttribute(TallyXmlNames.VoucherTypeAttribute, voucher.VoucherTypeName),
            new XAttribute(TallyXmlNames.ActionAttribute, TallyConventions.CreateAction));

        if (voucher.Id is { } id)
        {
            element.Add(new XElement(TallyXmlNames.Guid, TallyConventions.GuidText(id)));
        }

        element.Add(new XElement(TallyXmlNames.Date, TallyConventions.Date(voucher.Date)));
        element.Add(new XElement(TallyXmlNames.EffectiveDate, TallyConventions.Date(voucher.Date)));
        element.Add(new XElement(TallyXmlNames.VoucherTypeName, voucher.VoucherTypeName));
        element.Add(new XElement(TallyXmlNames.VoucherNumber, voucher.Number));
        element.Add(new XElement(
            TallyXmlNames.PersistedView,
            voucher.InventoryEntries.Count > 0 ? TallyXmlNames.InvoiceView : TallyXmlNames.AccountingView));

        AddOptional(element, TallyXmlNames.Narration, voucher.Narration);
        AddOptional(element, TallyXmlNames.Reference, voucher.ReferenceNumber);

        if (voucher.ReferenceDate is { } referenceDate)
        {
            element.Add(new XElement(TallyXmlNames.ReferenceDate, TallyConventions.Date(referenceDate)));
        }

        // Written on every voucher, not only cancelled ones. A re-import overwrites by GUID, so a
        // voucher cancelled after a previous export has to carry the flag that clears it — omitting
        // the element on a live voucher would leave a stale cancellation standing in Tally.
        element.Add(new XElement(TallyXmlNames.IsCancelled, TallyConventions.Bool(voucher.IsCancelled)));

        foreach (var entry in voucher.LedgerEntries)
        {
            element.Add(LedgerEntry(entry));
        }

        foreach (var entry in voucher.InventoryEntries)
        {
            element.Add(InventoryEntry(entry));
        }

        return element;
    }

    private static XElement LedgerEntry(InteropLedgerEntry entry)
    {
        var element = new XElement(
            TallyXmlNames.LedgerEntries,
            new XElement(TallyXmlNames.LedgerName, entry.LedgerName),
            new XElement(TallyXmlNames.IsDeemedPositive, TallyConventions.DeemedPositive(entry.AmountMinor)),
            new XElement(TallyXmlNames.Amount, TallyConventions.Amount(entry.AmountMinor)));

        AddOptional(element, TallyXmlNames.Narration, entry.Narration);

        if (!string.IsNullOrWhiteSpace(entry.CostCentreName))
        {
            element.Add(new XElement(
                TallyXmlNames.CostCentreList,
                new XElement(TallyXmlNames.Name, entry.CostCentreName),
                new XElement(TallyXmlNames.Amount, TallyConventions.Amount(entry.AmountMinor))));
        }

        return element;
    }

    private static XElement InventoryEntry(InteropInventoryEntry entry)
    {
        var quantity = TallyConventions.Quantity(entry.QuantityMinor, entry.UnitCode);

        var element = new XElement(
            TallyXmlNames.InventoryEntries,
            new XElement(TallyXmlNames.StockItemName, entry.StockItemName),
            new XElement(TallyXmlNames.ActualQuantity, quantity),
            new XElement(TallyXmlNames.BilledQuantity, quantity),
            new XElement(TallyXmlNames.Amount, TallyConventions.Amount(entry.AmountMinor)));

        if (entry.RateMinor is { } rate)
        {
            element.Add(new XElement(TallyXmlNames.Rate, TallyConventions.Quantity(rate, entry.UnitCode)));
        }

        if (!string.IsNullOrWhiteSpace(entry.GodownName))
        {
            element.Add(new XElement(
                TallyXmlNames.BatchAllocations,
                new XElement(TallyXmlNames.GodownName, entry.GodownName),
                new XElement(TallyXmlNames.BatchName, entry.BatchName ?? string.Empty),
                new XElement(TallyXmlNames.ActualQuantity, quantity),
                new XElement(TallyXmlNames.BilledQuantity, quantity),
                new XElement(TallyXmlNames.Amount, TallyConventions.Amount(entry.AmountMinor))));
        }

        return element;
    }

    private static XElement Master(string elementName, string name, Guid? id)
    {
        var element = new XElement(
            elementName,
            new XAttribute(TallyXmlNames.NameAttribute, name),
            new XAttribute(TallyXmlNames.ActionAttribute, TallyConventions.CreateAction),
            new XElement(TallyXmlNames.Name, name));

        if (id is { } value)
        {
            element.Add(new XElement(TallyXmlNames.Guid, TallyConventions.GuidText(value)));
        }

        return element;
    }

    private static void AddOptional(XElement element, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            element.Add(new XElement(name, value));
        }
    }

    private static void AddActive(XElement element, bool isActive) =>
        element.Add(new XElement(TallyXmlNames.IsActive, TallyConventions.Bool(isActive)));
}
