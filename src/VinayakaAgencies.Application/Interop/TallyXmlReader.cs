using System.Globalization;
using System.Xml.Linq;
using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// Parses Tally's XML interchange format into the canonical model.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="TallyXmlWriter"/>, sharing its element names through
/// <see cref="TallyXmlNames"/> and its encodings through <see cref="TallyConventions"/>. That
/// sharing is what makes the C8.2 round-trip a test of the model rather than of two independently
/// written files agreeing.
/// </para>
/// <para>
/// Nothing here throws on bad data. An element that cannot be understood becomes an
/// <see cref="InteropRecord.Unreadable"/> carrying its line number and a reason, because the import
/// has to report every problem in the file at once (C8.4) rather than stopping at the first.
/// </para>
/// </remarks>
public static class TallyXmlReader
{
    /// <summary>
    /// Reads a whole document into memory.
    /// </summary>
    /// <remarks>
    /// For small files and for tests. The import path uses <see cref="TallyXmlSource"/>, which
    /// streams; a full history is exactly what this overload should not be pointed at.
    /// </remarks>
    public static InteropDocument Read(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        var root = XDocument.Parse(xml, LoadOptions.SetLineInfo).Root
            ?? throw new InvalidOperationException("The file has no root element, so it is not a Tally export.");

        var requestDesc = root
            .Descendants(TallyXmlNames.RequestDesc)
            .FirstOrDefault();

        var staticVariables = requestDesc?.Element(TallyXmlNames.StaticVariables);

        var records = root
            .Descendants(TallyXmlNames.TallyMessage)
            .SelectMany(message => message.Elements())
            .Select((element, index) => ParseMessage(element, LocationOf(element, index + 1)))
            .ToList();

        return Compose(
            companyName: staticVariables?.Element(TallyXmlNames.CurrentCompany)?.Value ?? string.Empty,
            fromDate: TallyConventions.ParseDate(staticVariables?.Element(TallyXmlNames.FromDate)?.Value),
            toDate: TallyConventions.ParseDate(staticVariables?.Element(TallyXmlNames.ToDate)?.Value),
            records);
    }

    /// <summary>
    /// Folds a sequence of records back into a document.
    /// </summary>
    /// <remarks>
    /// Shared with <see cref="TallyJsonSource"/> and used by tests that want to assert on a whole
    /// parsed document rather than on a stream. Unreadable records are dropped here — a document is
    /// what survived parsing, and the reporting of what did not is the importer's job, not this
    /// type's.
    /// </remarks>
    public static InteropDocument Compose(
        string companyName,
        DateOnly? fromDate,
        DateOnly? toDate,
        IEnumerable<InteropRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var materialised = records as IReadOnlyCollection<InteropRecord> ?? [.. records];

        return new InteropDocument
        {
            CompanyName = companyName,
            FromDate = fromDate,
            ToDate = toDate,
            AccountGroups = [.. materialised.OfType<InteropRecord.AccountGroup>().Select(r => r.Value)],
            Ledgers = [.. materialised.OfType<InteropRecord.Ledger>().Select(r => r.Value)],
            Units = [.. materialised.OfType<InteropRecord.Unit>().Select(r => r.Value)],
            StockGroups = [.. materialised.OfType<InteropRecord.StockGroup>().Select(r => r.Value)],
            Godowns = [.. materialised.OfType<InteropRecord.Godown>().Select(r => r.Value)],
            Items = [.. materialised.OfType<InteropRecord.Item>().Select(r => r.Value)],
            VoucherTypes = [.. materialised.OfType<InteropRecord.VoucherType>().Select(r => r.Value)],
            Vouchers = [.. materialised.OfType<InteropRecord.Voucher>().Select(r => r.Value)],
        };
    }

    /// <summary>Turns one master or voucher element into a record.</summary>
    public static InteropRecord ParseMessage(XElement element, InteropSourceLocation location)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(location);

        return element.Name.LocalName switch
        {
            TallyXmlNames.Group => ParseAccountGroup(element, location),
            TallyXmlNames.Ledger => ParseLedger(element, location),
            TallyXmlNames.Unit => ParseUnit(element, location),
            TallyXmlNames.StockGroup => ParseStockGroup(element, location),
            TallyXmlNames.Godown => ParseGodown(element, location),
            TallyXmlNames.StockItem => ParseStockItem(element, location),
            TallyXmlNames.VoucherTypeElement => ParseVoucherType(element, location),
            TallyXmlNames.Voucher => ParseVoucher(element, location),
            var other => new InteropRecord.Unreadable(
                location,
                $"'{other}' is not a master or voucher this import understands.",
                other),
        };
    }

    /// <summary>The line an element sits on, when the reader kept line information.</summary>
    public static InteropSourceLocation LocationOf(XElement element, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element is System.Xml.IXmlLineInfo info && info.HasLineInfo()
            ? InteropSourceLocation.AtLine(ordinal, info.LineNumber)
            : new InteropSourceLocation(ordinal, $"record {ordinal.ToString(CultureInfo.InvariantCulture)}");
    }

    private static InteropRecord ParseAccountGroup(XElement element, InteropSourceLocation location)
    {
        var name = Text(element, TallyXmlNames.Name) ?? Attribute(element, TallyXmlNames.NameAttribute);

        if (string.IsNullOrWhiteSpace(name))
        {
            return Unreadable(location, TallyXmlNames.Group, "the group has no name.");
        }

        var natureText = Text(element, TallyXmlNames.NatureOfGroup);

        // A group with no nature and no parent in the file cannot be placed on either statement.
        // Defaulting it would put real balances on the wrong side of the accounts silently, so it is
        // reported instead; a group whose parent is present inherits, and the validator resolves that.
        var parent = Text(element, TallyXmlNames.Parent);
        var hasParent = !string.IsNullOrWhiteSpace(parent);

        if (!Enum.TryParse<AccountNature>(natureText, ignoreCase: true, out var nature) && !hasParent)
        {
            return Unreadable(
                location,
                TallyXmlNames.Group,
                $"group '{name}' declares no parent and no recognised nature, so it cannot be placed "
                + "under assets, liabilities, income or expenses.");
        }

        return new InteropRecord.AccountGroup(
            location,
            new InteropAccountGroup(
                name.Trim(),
                hasParent ? parent!.Trim() : null,
                nature,
                TallyConventions.ParseGuid(Text(element, TallyXmlNames.Guid)),
                TallyConventions.ParseBool(Text(element, TallyXmlNames.AffectsGrossProfit)),
                IsActive(element)));
    }

    private static InteropRecord ParseLedger(XElement element, InteropSourceLocation location)
    {
        var name = Text(element, TallyXmlNames.Name) ?? Attribute(element, TallyXmlNames.NameAttribute);

        if (string.IsNullOrWhiteSpace(name))
        {
            return Unreadable(location, TallyXmlNames.Ledger, "the ledger has no name.");
        }

        var parent = Text(element, TallyXmlNames.Parent);

        if (string.IsNullOrWhiteSpace(parent))
        {
            return Unreadable(
                location,
                TallyXmlNames.Ledger,
                $"ledger '{name}' names no group, and a ledger outside the chart of accounts has "
                + "nowhere to post.");
        }

        return new InteropRecord.Ledger(
            location,
            new InteropLedger(
                name.Trim(),
                parent.Trim(),
                TallyConventions.ParseGuid(Text(element, TallyXmlNames.Guid)),
                Text(element, TallyXmlNames.Alias),
                Text(element, TallyXmlNames.Description),
                Text(element, TallyXmlNames.PartyGstin),
                IsActive(element)));
    }

    private static InteropRecord ParseUnit(XElement element, InteropSourceLocation location)
    {
        var code = Text(element, TallyXmlNames.Name) ?? Attribute(element, TallyXmlNames.NameAttribute);

        if (string.IsNullOrWhiteSpace(code))
        {
            return Unreadable(location, TallyXmlNames.Unit, "the unit has no symbol.");
        }

        var places = 0;
        var placesText = Text(element, TallyXmlNames.DecimalPlaces);

        if (!string.IsNullOrWhiteSpace(placesText)
            && !int.TryParse(placesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out places))
        {
            return Unreadable(
                location,
                TallyXmlNames.Unit,
                $"unit '{code}' has a decimal-places value of '{placesText}', which is not a whole number.");
        }

        return new InteropRecord.Unit(
            location,
            new InteropUnit(
                code.Trim(),
                places,
                TallyConventions.ParseGuid(Text(element, TallyXmlNames.Guid)),
                Text(element, TallyXmlNames.Description),
                IsActive(element)));
    }

    private static InteropRecord ParseStockGroup(XElement element, InteropSourceLocation location)
    {
        var name = Text(element, TallyXmlNames.Name) ?? Attribute(element, TallyXmlNames.NameAttribute);

        return string.IsNullOrWhiteSpace(name)
            ? Unreadable(location, TallyXmlNames.StockGroup, "the stock group has no name.")
            : new InteropRecord.StockGroup(
                location,
                new InteropStockGroup(
                    name.Trim(),
                    TallyConventions.ParseGuid(Text(element, TallyXmlNames.Guid)),
                    Text(element, TallyXmlNames.Description),
                    IsActive(element)));
    }

    private static InteropRecord ParseGodown(XElement element, InteropSourceLocation location)
    {
        var name = Text(element, TallyXmlNames.Name) ?? Attribute(element, TallyXmlNames.NameAttribute);

        return string.IsNullOrWhiteSpace(name)
            ? Unreadable(location, TallyXmlNames.Godown, "the godown has no name.")
            : new InteropRecord.Godown(
                location,
                new InteropGodown(
                    name.Trim(),
                    TallyConventions.ParseGuid(Text(element, TallyXmlNames.Guid)),
                    Text(element, TallyXmlNames.Address),
                    IsActive(element)));
    }

    private static InteropRecord ParseStockItem(XElement element, InteropSourceLocation location)
    {
        var name = Text(element, TallyXmlNames.Name) ?? Attribute(element, TallyXmlNames.NameAttribute);

        if (string.IsNullOrWhiteSpace(name))
        {
            return Unreadable(location, TallyXmlNames.StockItem, "the stock item has no name.");
        }

        var baseUnit = Text(element, TallyXmlNames.BaseUnits);

        if (string.IsNullOrWhiteSpace(baseUnit))
        {
            return Unreadable(
                location,
                TallyXmlNames.StockItem,
                $"stock item '{name}' names no base unit, so its quantities have no meaning.");
        }

        var parent = Text(element, TallyXmlNames.Parent);

        return new InteropRecord.Item(
            location,
            new InteropItem(
                name.Trim(),
                baseUnit.Trim(),
                TallyConventions.ParseGuid(Text(element, TallyXmlNames.Guid)),
                Text(element, TallyXmlNames.PartNumber),
                string.IsNullOrWhiteSpace(parent) ? null : parent.Trim(),
                Text(element, TallyXmlNames.HsnCode),
                IsActive(element)));
    }

    private static InteropRecord ParseVoucherType(XElement element, InteropSourceLocation location)
    {
        var name = Text(element, TallyXmlNames.Name) ?? Attribute(element, TallyXmlNames.NameAttribute);

        if (string.IsNullOrWhiteSpace(name))
        {
            return Unreadable(location, TallyXmlNames.VoucherTypeElement, "the voucher type has no name.");
        }

        var parent = Text(element, TallyXmlNames.Parent);
        var kind = TallyConventions.ParseVoucherKind(parent) ?? TallyConventions.ParseVoucherKind(name);

        if (kind is null)
        {
            return Unreadable(
                location,
                TallyXmlNames.VoucherTypeElement,
                $"voucher type '{name}' inherits from '{parent}', which is not one of the eight "
                + "behaviours this application posts. A voucher type has to say which of them it is.");
        }

        return new InteropRecord.VoucherType(
            location,
            new InteropVoucherType(
                name.Trim(),
                kind.Value,
                TallyConventions.ParseGuid(Text(element, TallyXmlNames.Guid)),
                Text(element.Element(TallyXmlNames.PrefixDetails), TallyXmlNames.Prefix),
                IsActive(element)));
    }

    private static InteropRecord ParseVoucher(XElement element, InteropSourceLocation location)
    {
        var typeName = Text(element, TallyXmlNames.VoucherTypeName)
            ?? Attribute(element, TallyXmlNames.VoucherTypeAttribute);

        if (string.IsNullOrWhiteSpace(typeName))
        {
            return Unreadable(location, TallyXmlNames.Voucher, "the voucher declares no voucher type.");
        }

        // Null when the name is not one of Tally's eight. That is normal — a company's own voucher
        // types are declared by the VOUCHERTYPE masters in the same file, and the importer resolves
        // against those rather than having the reader guess a behaviour from a name.
        var kind = TallyConventions.ParseVoucherKind(typeName);

        var date = TallyConventions.ParseDate(Text(element, TallyXmlNames.Date));

        if (date is null)
        {
            return Unreadable(
                location,
                TallyXmlNames.Voucher,
                $"voucher '{Text(element, TallyXmlNames.VoucherNumber)}' has no readable date.");
        }

        var ledgerEntries = new List<InteropLedgerEntry>();

        foreach (var entryElement in element.Elements(TallyXmlNames.LedgerEntries))
        {
            var ledgerName = Text(entryElement, TallyXmlNames.LedgerName);
            var amount = TallyConventions.ParseAmount(Text(entryElement, TallyXmlNames.Amount));

            if (string.IsNullOrWhiteSpace(ledgerName) || amount is null)
            {
                return Unreadable(
                    location,
                    TallyXmlNames.Voucher,
                    $"a ledger entry on voucher '{Text(element, TallyXmlNames.VoucherNumber)}' is "
                    + "missing its ledger name or its amount.");
            }

            ledgerEntries.Add(new InteropLedgerEntry(
                ledgerName.Trim(),
                amount.Value,
                Text(entryElement.Element(TallyXmlNames.CostCentreList), TallyXmlNames.Name),
                Text(entryElement, TallyXmlNames.Narration)));
        }

        var inventoryEntries = new List<InteropInventoryEntry>();

        foreach (var entryElement in element.Elements(TallyXmlNames.InventoryEntries))
        {
            var itemName = Text(entryElement, TallyXmlNames.StockItemName);
            var (quantity, unit) = TallyConventions.ParseQuantity(Text(entryElement, TallyXmlNames.ActualQuantity));
            var amount = TallyConventions.ParseAmount(Text(entryElement, TallyXmlNames.Amount));

            if (string.IsNullOrWhiteSpace(itemName) || quantity is null || amount is null)
            {
                return Unreadable(
                    location,
                    TallyXmlNames.Voucher,
                    $"a stock entry on voucher '{Text(element, TallyXmlNames.VoucherNumber)}' is "
                    + "missing its item name, quantity or amount.");
            }

            var (rate, _) = TallyConventions.ParseQuantity(Text(entryElement, TallyXmlNames.Rate));

            var allocation = entryElement.Element(TallyXmlNames.BatchAllocations);

            inventoryEntries.Add(new InteropInventoryEntry(
                itemName.Trim(),
                quantity.Value,
                amount.Value,
                Text(allocation, TallyXmlNames.GodownName),
                unit,
                rate,
                Text(allocation, TallyXmlNames.BatchName)));
        }

        return new InteropRecord.Voucher(
            location,
            new InteropVoucher
            {
                Id = TallyConventions.ParseGuid(Text(element, TallyXmlNames.Guid)),
                VoucherTypeName = typeName.Trim(),
                Kind = kind,
                Date = date.Value,
                Number = Text(element, TallyXmlNames.VoucherNumber)?.Trim() ?? string.Empty,
                Narration = Text(element, TallyXmlNames.Narration),
                ReferenceNumber = Text(element, TallyXmlNames.Reference),
                ReferenceDate = TallyConventions.ParseDate(Text(element, TallyXmlNames.ReferenceDate)),
                IsCancelled = TallyConventions.ParseBool(Text(element, TallyXmlNames.IsCancelled)),
                LedgerEntries = ledgerEntries,
                InventoryEntries = inventoryEntries,
            });
    }

    private static InteropRecord.Unreadable Unreadable(InteropSourceLocation location, string kind, string reason) =>
        new InteropRecord.Unreadable(location, reason, kind);

    private static bool IsActive(XElement element)
    {
        var text = Text(element, TallyXmlNames.IsActive);

        // Absent means active. Tally omits the element on a live master, and reading "not stated" as
        // "deactivated" would import an entire chart of accounts switched off.
        return string.IsNullOrWhiteSpace(text) || TallyConventions.ParseBool(text);
    }

    private static string? Text(XElement? element, string name)
    {
        var value = element?.Element(name)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? Attribute(XElement element, string name)
    {
        var value = element.Attribute(name)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
