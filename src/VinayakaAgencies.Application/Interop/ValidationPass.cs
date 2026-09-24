using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// The first of the importer's two passes: everything wrong with the file, in one read.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here writes. It walks the records, resolves every reference each one makes, and collects
/// what it could not resolve. A record it accepts is registered in the index as though it had been
/// written — that is what lets a file create a unit on one line and a stock item naming it forty
/// lines later without being told the unit is missing.
/// </para>
/// <para>
/// The distinction it draws between a problem and a warning is the one C8.4 turns on. A problem
/// stops the whole file; a warning is something the owner should know and then decide about. Getting
/// that line in the wrong place is how an importer becomes either unusable (everything blocks) or
/// untrustworthy (nothing does).
/// </para>
/// </remarks>
internal sealed class ValidationPass
{
    private readonly InteropMasterIndex _index;
    private readonly InteropImportOptions _options;
    private readonly List<InteropImportProblem> _problems = [];
    private readonly List<InteropImportWarning> _warnings = [];
    private readonly Dictionary<string, Counter> _tallies = new(StringComparer.Ordinal);
    private readonly HashSet<string> _voucherKeysInFile = new(StringComparer.Ordinal);

    private long _debitMinor;
    private long _creditMinor;
    private long _stockValueMinor;

    public ValidationPass(InteropMasterIndex index, InteropImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(options);

        _index = index;
        _options = options;
    }

    public void Accept(InteropRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        switch (record)
        {
            case InteropRecord.Unreadable unreadable:
                _problems.Add(new InteropImportProblem(
                    unreadable.Location,
                    unreadable.Kind,
                    string.Empty,
                    unreadable.Reason));
                break;

            case InteropRecord.Unit unit when _options.ImportMasters:
                AcceptUnit(unit);
                break;

            case InteropRecord.StockGroup group when _options.ImportMasters:
                AcceptStockGroup(group);
                break;

            case InteropRecord.Godown godown when _options.ImportMasters:
                AcceptGodown(godown);
                break;

            case InteropRecord.Item item when _options.ImportMasters:
                AcceptStockItem(item);
                break;

            case InteropRecord.AccountGroup accountGroup when _options.ImportMasters:
                AcceptAccountGroup(accountGroup);
                break;

            case InteropRecord.Ledger ledger when _options.ImportMasters:
                AcceptLedger(ledger);
                break;

            // A voucher type is a master, but it arrives with the vouchers it carries and is useless
            // without them, so either selection pulls it in. Refusing to create it under
            // "vouchers only" would refuse every voucher filed under it.
            case InteropRecord.VoucherType voucherType:
                AcceptVoucherType(voucherType);
                break;

            case InteropRecord.Voucher voucher when _options.ImportVouchers:
                AcceptVoucher(voucher);
                break;

            default:
                // Deselected by the options. Not counted, not reported: the owner asked for masters
                // only, and listing every voucher they chose to leave out is noise, not a report.
                break;
        }
    }

    public InteropImportPreview ToPreview(IInteropSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new InteropImportPreview(
            source.Description,
            source.FormatName,
            _problems,
            _warnings,
            [.. _tallies.Select(pair => new InteropRecordTally(pair.Key, pair.Value.Created, pair.Value.Updated))],
            new InteropTotals(_debitMinor, _creditMinor, _stockValueMinor));
    }

    private void AcceptUnit(InteropRecord.Unit record)
    {
        var value = record.Value;

        if (ReportAmbiguity(record, _index.Units, value.Id, value.Code))
        {
            return;
        }

        if (value.DecimalPlaces is < 0 or > MoneyScale.Places)
        {
            Problem(record, $"a unit's decimal places must be between 0 and {MoneyScale.Places}.");
            return;
        }

        Count(record, _index.Units.Match(value.Id, value.Code) is not null);
        _index.Units.Register(value.Id ?? Guid.NewGuid(), value.Code);
    }

    private void AcceptStockGroup(InteropRecord.StockGroup record)
    {
        var value = record.Value;

        if (ReportAmbiguity(record, _index.StockGroups, value.Id, value.Name))
        {
            return;
        }

        Count(record, _index.StockGroups.Match(value.Id, value.Name) is not null);
        _index.StockGroups.Register(value.Id ?? Guid.NewGuid(), value.Name);
    }

    private void AcceptGodown(InteropRecord.Godown record)
    {
        var value = record.Value;

        if (ReportAmbiguity(record, _index.Godowns, value.Id, value.Name))
        {
            return;
        }

        Count(record, _index.Godowns.Match(value.Id, value.Name) is not null);
        _index.Godowns.Register(value.Id ?? Guid.NewGuid(), value.Name);
    }

    private void AcceptStockItem(InteropRecord.Item record)
    {
        var value = record.Value;

        if (ReportAmbiguity(record, _index.Items, value.Id, value.Name))
        {
            return;
        }

        if (!_index.Units.Knows(value.BaseUnitCode))
        {
            Problem(
                record,
                $"its base unit '{value.BaseUnitCode}' is neither in the books nor created earlier in "
                + "this file. Create the unit first, or add it to the file above this line.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(value.StockGroupName) && !_index.StockGroups.Knows(value.StockGroupName))
        {
            Problem(record, $"its stock group '{value.StockGroupName}' does not exist.");
            return;
        }

        Count(record, _index.Items.Match(value.Id, value.Name) is not null);
        _index.Items.Register(value.Id ?? Guid.NewGuid(), value.Name);

        if (!string.IsNullOrWhiteSpace(value.HsnSacCode))
        {
            _index.HsnCodes.Register(Guid.NewGuid(), value.HsnSacCode);
        }
    }

    private void AcceptAccountGroup(InteropRecord.AccountGroup record)
    {
        var value = record.Value;

        if (!string.IsNullOrWhiteSpace(value.ParentName) && _index.ResolveAccountGroup(value.ParentName) is null)
        {
            Problem(
                record,
                $"its parent group '{value.ParentName}' is neither in the chart of accounts nor "
                + "created earlier in this file.");
            return;
        }

        Count(record, _index.FindAccountGroup(value.Id, value.Name) is not null);

        // Registered as a real group so a later ledger or child group in the same file resolves. The
        // nature and path are the ones the writer will use, so the validation sees what will happen
        // rather than an approximation of it.
        var parent = _index.ResolveAccountGroup(value.ParentName);

        if (_index.FindAccountGroup(value.Id, value.Name) is null)
        {
            var provisional = new Domain.Accounting.AccountGroup(
                _index.CompanyId,
                value.Name,
                parent?.Nature ?? value.Nature,
                parent?.Id,
                parent?.AffectsGrossProfit ?? value.AffectsGrossProfit);

            provisional.SetMaterialisedPath(parent?.MaterialisedPath);
            _index.RegisterAccountGroup(provisional);
        }
    }

    private void AcceptLedger(InteropRecord.Ledger record)
    {
        var value = record.Value;

        if (ReportAmbiguity(record, _index.Ledgers, value.Id, value.Name))
        {
            return;
        }

        if (_index.ResolveAccountGroup(value.ParentGroupName) is null)
        {
            Problem(
                record,
                $"its group '{value.ParentGroupName}' is neither in the chart of accounts nor created "
                + "earlier in this file, so the ledger has nowhere to sit.");
            return;
        }

        Count(record, _index.Ledgers.Match(value.Id, value.Name) is not null);
        _index.Ledgers.Register(value.Id ?? Guid.NewGuid(), value.Name);
    }

    private void AcceptVoucherType(InteropRecord.VoucherType record)
    {
        var value = record.Value;
        var key = NameNormaliser.Normalise(value.Name);
        var known = _index.VoucherTypesByName.ContainsKey(key);

        Count(record, known);

        if (!known)
        {
            // Registered provisionally so the vouchers below it in the file resolve. The writer will
            // create the real row, together with the numbering series a type cannot post without.
            _index.RegisterProvisionalVoucherType(value.Name, value.Kind);
        }
    }

    private void AcceptVoucher(InteropRecord.Voucher record)
    {
        var value = record.Value;
        var type = _index.ResolveVoucherType(value.VoucherTypeName, value.Kind);

        if (type is null)
        {
            Problem(
                record,
                $"voucher type '{value.VoucherTypeName}' does not exist in this company, and the file "
                + "does not define it. Add the voucher type to the file, or create it here first.");
            return;
        }

        var key = _index.VoucherKey(type.Id, value.Number, value.Date);

        if (!_voucherKeysInFile.Add(key))
        {
            Problem(
                record,
                $"a second {value.VoucherTypeName} numbered '{value.Number}' on {value.Date:yyyy-MM-dd} "
                + "appears in this file. Voucher numbers are unique within a series and a financial "
                + "year, so one of the two is wrong.");
            return;
        }

        var existing = value.Id is { } id && _index.VoucherIds.Contains(id)
            ? id
            : _index.VoucherIdByTypeNumberAndYear.TryGetValue(key, out var found) ? found : (Guid?)null;

        if (value.IsCancelled)
        {
            if (existing is null)
            {
                Warning(
                    record,
                    "the voucher is marked cancelled and the books have never held it, so there is "
                    + "nothing to cancel. It will be skipped.");
                return;
            }

            Count(record, updated: true);
            return;
        }

        if (value.LedgerEntries.Count == 0)
        {
            Problem(record, "the voucher posts to no ledger at all.");
            return;
        }

        if (!value.IsBalanced)
        {
            Problem(
                record,
                $"debits and credits differ by {Money(value.NetMinor)}. A voucher that does not "
                + "balance cannot be posted.");
            return;
        }

        foreach (var entry in value.LedgerEntries)
        {
            if (!_index.Ledgers.Knows(entry.LedgerName))
            {
                Problem(
                    record,
                    $"it posts to ledger '{entry.LedgerName}', which is neither in the chart of "
                    + "accounts nor created earlier in this file.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(entry.CostCentreName) && !_index.CostCentres.Knows(entry.CostCentreName))
            {
                Problem(record, $"it names cost centre '{entry.CostCentreName}', which does not exist.");
                return;
            }
        }

        if (!AcceptInventory(record, value))
        {
            return;
        }

        _debitMinor += value.TotalDebitMinor;
        _creditMinor += value.TotalCreditMinor;
        Count(record, existing is not null);
    }

    private bool AcceptInventory(InteropRecord.Voucher record, InteropVoucher value)
    {
        foreach (var entry in value.InventoryEntries)
        {
            if (!_index.Items.Knows(entry.StockItemName))
            {
                Problem(
                    record,
                    $"it moves stock item '{entry.StockItemName}', which is neither in the item master "
                    + "nor created earlier in this file.");
                return false;
            }

            if (entry.QuantityMinor == 0L)
            {
                Problem(record, $"the stock line for '{entry.StockItemName}' carries no quantity.");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(entry.GodownName) && !_index.Godowns.Knows(entry.GodownName))
            {
                Problem(record, $"it names godown '{entry.GodownName}', which does not exist.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(entry.GodownName) && !_index.Godowns.Knows(Godown.MainLocationName))
            {
                Problem(
                    record,
                    $"the stock line for '{entry.StockItemName}' names no godown, and this company has "
                    + $"no '{Godown.MainLocationName}' to fall back on.");
                return false;
            }

            var itemId = _index.Items.Resolve(entry.StockItemName);

            if (itemId is { } id && _index.ItemsById.TryGetValue(id, out var item))
            {
                if (item.IsBatchTracked
                    && entry.QuantityMinor > 0L
                    && string.IsNullOrWhiteSpace(entry.BatchName))
                {
                    Problem(
                        record,
                        $"'{item.Name}' is batch-tracked and this receipt carries no batch number.");
                    return false;
                }

                if (item.IsSerialTracked)
                {
                    // A warning, not a problem, and the reasoning is in InteropImportWarning: the
                    // file has no serials to give, refusing the row would make a serial-tracking
                    // business unable to migrate, and importing it silently would leave stock the
                    // serial lifecycle cannot account for.
                    Warning(
                        record,
                        $"'{item.Name}' is serial-tracked, and the file carries no serial numbers. The "
                        + "quantity will be imported without them, so its serials must be recorded "
                        + "separately before it can be issued.");
                }
            }

            _stockValueMinor += StockLedgerEntry.ComputeValueMinor(
                MoneyScale.FromMinor(entry.QuantityMinor),
                entry.RateMinor ?? 0L);
        }

        return true;
    }

    private bool ReportAmbiguity(InteropRecord record, InteropNameMap map, Guid? id, string name)
    {
        if (!map.IsAmbiguous(id, name))
        {
            return false;
        }

        Problem(
            record,
            $"its identifier matches one {map.Kind} in the books while its name matches a different "
            + "one. Importing it would either rename a record onto a name already in use or change "
            + "the wrong record, so it is refused rather than guessed at.");

        return true;
    }

    private void Problem(InteropRecord record, string reason) =>
        _problems.Add(new InteropImportProblem(record.Location, record.Kind, record.Label, reason));

    private void Warning(InteropRecord record, string note) =>
        _warnings.Add(new InteropImportWarning(record.Location, record.Kind, record.Label, note));

    private void Count(InteropRecord record, bool updated)
    {
        if (!_tallies.TryGetValue(record.Kind, out var counter))
        {
            counter = new Counter();
            _tallies[record.Kind] = counter;
        }

        if (updated)
        {
            counter.Updated++;
        }
        else
        {
            counter.Created++;
        }
    }

    private static string Money(long minor) => MoneyScale.ToInvariantString(MoneyScale.FromMinor(minor));

    private sealed class Counter
    {
        public int Created { get; set; }

        public int Updated { get; set; }
    }
}
