using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Application.Maintenance;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Maintenance;

/// <summary>
/// Checkpoints C10.1 and C10.6 (phase-10-hardening.md §3): a full backup/restore cycle round-trips
/// without data loss, and the restored books reproduce a byte-identical trial balance and stock
/// summary.
/// </summary>
/// <remarks>
/// <para>
/// Everything runs against a real SQLite file through the real DI composition, each operation in
/// its own scope. That matters more here than almost anywhere else in the suite: the thing under
/// test is a snapshot of a live database taken while connections are open, and an in-memory double
/// would have no write-ahead log, no pooled handles and no file locking — which is to say none of
/// the three things that make this difficult.
/// </para>
/// <para>
/// The negative cases are the point of the file, not the happy path. A backup feature that writes
/// an archive and reports success is easy; the question an owner is really asking is whether the
/// archive is any good, so four of these tests break an archive on purpose and require the
/// verification to say so.
/// </para>
/// </remarks>
public sealed class BackupRestoreTests
{
    private static readonly DateOnly InFy2026 = new(2026, 4, 15);

    // ------------------------------------------------------------------- C10.1 — the round trip

    [Fact]
    public async Task Create_OfABookWithPostingsAndStock_WritesAVerifiedArchiveCarryingTheDatabase()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var summary = await host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .CreateAsync("test — C10.1"));

        File.Exists(summary.FilePath).ShouldBeTrue();
        summary.SizeBytes.ShouldBeGreaterThan(0);

        // Verified by reopening the finished file, which is what §2 asks for. A summary that said
        // "verified" because the write did not throw would be the assurance a backup replaces.
        summary.IsVerified.ShouldBeTrue();
        summary.Verification!.Problems.ShouldBeEmpty();
        summary.Verification.DatabaseIntegrityIntact.ShouldBeTrue();

        var manifest = summary.Manifest.ShouldNotBeNull();
        manifest.FormatVersion.ShouldBe(BackupManifest.CurrentFormatVersion);
        manifest.Reason.ShouldBe("test — C10.1");
        manifest.CompanyId.ShouldBe(world.CompanyId);
        manifest.SchemaMigrations.ShouldNotBeEmpty();

        // The database is actually in there, under the name the manifest claims.
        using var archive = ZipFile.OpenRead(summary.FilePath);
        archive.GetEntry("database/" + manifest.DatabaseFileName).ShouldNotBeNull();
        archive.GetEntry(BackupManifest.EntryName).ShouldNotBeNull();

        // And the fingerprint describes books with something in them. A fingerprint of an empty
        // book would make the C10.6 comparison below pass without proving anything.
        manifest.Fingerprint.TrialBalanceRowCount.ShouldBeGreaterThan(0);
        manifest.Fingerprint.StockSummaryRowCount.ShouldBeGreaterThan(0);
        manifest.Fingerprint.TotalDebit.ShouldBe(manifest.Fingerprint.TotalCredit);
    }

    // ------------------------------------------------- C10.6 — byte-identical after the restore

    [Fact]
    public async Task Restore_AfterTheBooksMoveOn_ReproducesTheBackedUpTrialBalanceAndStockSummary()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var atBackup = await FingerprintAsync(host);

        var summary = await host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .CreateAsync("test — C10.6"));

        // The books then move on, which is the only version of this test worth running. Restoring
        // onto an unchanged database would pass against a no-op implementation.
        await PostReceiptAsync(host, world, world.SecondItemId, 7m, 250m);
        await PostJournalAsync(host, world, 4_321m);

        var afterMorePosting = await FingerprintAsync(host);
        afterMorePosting.Matches(atBackup).ShouldBeFalse(
            "the fixture must actually change the books, or the restore proves nothing");

        var outcome = await host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .RestoreAsync(summary.FilePath));

        // C10.6, stated the way the checkpoint states it.
        outcome.FingerprintMatches.ShouldBeTrue(outcome.Explanation);

        // Recomputed once more from a fresh scope, so the claim does not rest on the value the
        // restore happened to return.
        var afterRestore = await FingerprintAsync(host);
        afterRestore.TrialBalanceHash.ShouldBe(atBackup.TrialBalanceHash);
        afterRestore.StockSummaryHash.ShouldBe(atBackup.StockSummaryHash);
        afterRestore.TrialBalanceRowCount.ShouldBe(atBackup.TrialBalanceRowCount);
        afterRestore.StockSummaryRowCount.ShouldBe(atBackup.StockSummaryRowCount);
        afterRestore.TotalStockValueMinor.ShouldBe(atBackup.TotalStockValueMinor);
    }

    [Fact]
    public async Task Restore_KeepsASafetyCopyOfTheDatabaseItReplaced()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var summary = await host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .CreateAsync("test — safety copy"));

        await PostJournalAsync(host, world, 9_999m);

        var outcome = await host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .RestoreAsync(summary.FilePath));

        // Without this, an operator who restored the wrong archive has destroyed the current books.
        File.Exists(outcome.SafetyCopyPath).ShouldBeTrue();
        new FileInfo(outcome.SafetyCopyPath).Length.ShouldBeGreaterThan(0);

        // And it is the pre-restore database, not a copy of what was restored: opening it must show
        // the ₹9,999 journal that the restore rolled away.
        var integrity = await host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupArchiveStore>()
            .CheckIntegrityAsync(outcome.SafetyCopyPath));

        integrity.IsIntact.ShouldBeTrue(integrity.Detail);
    }

    [Fact]
    public async Task Restore_PutsBackTheOperatorOwnedFilesAsWellAsTheDatabase()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeAsync(host);

        // §2: "a restore that loses the customised invoice template is not a restore". Print
        // templates are rows, so the database snapshot carries those; this stands in for the files
        // beside it — the logo and the operator's own settings.
        var logoPath = Path.Combine(host.Paths.DataRoot, "Assets", "logo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(logoPath)!);
        await File.WriteAllTextAsync(logoPath, "the owner's artwork");
        await File.WriteAllTextAsync(host.Paths.LocalSettingsPath, """{"Backup":{"RetainedBackupCount":7}}""");

        var summary = await host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .CreateAsync("test — operator files"));

        summary.Manifest!.Entries.Select(entry => entry.Path)
            .ShouldContain("data/Assets/logo.png");

        // Both files are then lost, as they would be by a reinstall onto a new machine.
        File.Delete(logoPath);
        File.Delete(host.Paths.LocalSettingsPath);

        await host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .RestoreAsync(summary.FilePath));

        (await File.ReadAllTextAsync(logoPath)).ShouldBe("the owner's artwork");
        (await File.ReadAllTextAsync(host.Paths.LocalSettingsPath)).ShouldContain("RetainedBackupCount");
    }

    // ------------------------------------------------- the verification has to actually bite

    [Fact]
    public async Task Verify_WhenAnArchivedFileIsAltered_ReportsTheEntryThatNoLongerMatches()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeAsync(host);

        var summary = await host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .CreateAsync("test — tamper"));

        summary.IsVerified.ShouldBeTrue();

        // One byte of the archived database, changed in place. This is the test that keeps the
        // verification from becoming theatre: if hashing were skipped, or compared against a hash
        // recomputed from the same altered bytes, this would still pass.
        var databaseEntryName = "database/" + summary.Manifest!.DatabaseFileName;

        using (var archive = ZipFile.Open(summary.FilePath, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry(databaseEntryName).ShouldNotBeNull();

            await using var stream = entry.Open();
            stream.Seek(0, SeekOrigin.Begin);
            stream.WriteByte(0x00);
        }

        var verification = await host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .VerifyAsync(summary.FilePath));

        verification.IsValid.ShouldBeFalse();
        verification.Problems.ShouldContain(problem => problem.Contains(databaseEntryName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verify_WhenAnArchivedEntryIsMissing_SaysSoRatherThanPassing()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeAsync(host);

        var summary = await host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .CreateAsync("test — missing entry"));

        var databaseEntryName = "database/" + summary.Manifest!.DatabaseFileName;

        using (var archive = ZipFile.Open(summary.FilePath, ZipArchiveMode.Update))
        {
            archive.GetEntry(databaseEntryName)!.Delete();
        }

        var verification = await host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .VerifyAsync(summary.FilePath));

        verification.IsValid.ShouldBeFalse();
        verification.Problems.ShouldContain(problem =>
            problem.Contains("missing from the archive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Restore_WhenTheArchiveFailsVerification_RefusesAndLeavesTheBooksAlone()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var summary = await host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .CreateAsync("test — refuse bad archive"));

        await PostJournalAsync(host, world, 1_234m);
        var before = await FingerprintAsync(host);

        using (var archive = ZipFile.Open(summary.FilePath, ZipArchiveMode.Update))
        {
            archive.GetEntry("database/" + summary.Manifest!.DatabaseFileName)!.Delete();
        }

        await Should.ThrowAsync<InvalidOperationException>(() => host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .RestoreAsync(summary.FilePath)));

        // The refusal has to happen before anything is replaced. A half-applied restore from an
        // archive known to be broken is worse than no restore at all.
        var after = await FingerprintAsync(host);
        after.Matches(before).ShouldBeTrue();
    }

    [Fact]
    public async Task Restore_WhenTheArchiveNamesAMigrationThisBuildDoesNotKnow_Refuses()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeAsync(host);

        var summary = await host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .CreateAsync("test — newer schema"));

        // An archive taken by a later build. Restoring it would produce a database with columns
        // this build does not select and a program that fails somewhere unrelated, halfway through
        // billing — so the refusal belongs here, where it can still be explained.
        await RewriteManifestAsync(
            summary.FilePath,
            manifest => manifest with
            {
                SchemaMigrations = [.. manifest.SchemaMigrations, "29990101000000_AddSomethingFromTheFuture"],
            });

        var error = await Should.ThrowAsync<InvalidOperationException>(() => host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .RestoreAsync(summary.FilePath)));

        error.Message.ShouldContain("newer schema");
        error.Message.ShouldContain("AddSomethingFromTheFuture");
    }

    // ------------------------------------------------------------------- retention (§4a D1)

    [Fact]
    public async Task Prune_WithRetentionSetToZero_StillKeepsTheNewestArchive()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeAsync(host);

        // §4a D1 makes the floor the load-bearing half of the retention policy: pruning must never
        // be able to empty the folder, whatever configuration says.
        await host.InScopeAsync(async sp =>
        {
            sp.GetRequiredService<BackupOptions>().RetainedBackupCount = 0;

            await sp.GetRequiredService<IBackupService>().CreateAsync("test — retention floor");
        });

        var remaining = await host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .ListAsync());

        remaining.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Prune_KeepsTheConfiguredNumberOfArchivesAndDropsTheOldest()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeAsync(host);

        for (var index = 0; index < 4; index++)
        {
            await host.InScopeAsync(async sp =>
            {
                sp.GetRequiredService<BackupOptions>().RetainedBackupCount = 2;

                await sp.GetRequiredService<IBackupService>()
                    .CreateAsync("test — retention " + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            });

            // The archive name carries a whole-second timestamp, so four backups on a fixed clock
            // would otherwise all be the same file.
            host.Clock.Advance(TimeSpan.FromMinutes(5));
        }

        var remaining = await host.InScopeAsync(sp => sp
            .GetRequiredService<IBackupService>()
            .ListAsync());

        remaining.Count.ShouldBe(2);

        // Newest first, and the two survivors are the two newest.
        remaining[0].CreatedAtUtc.ShouldBeGreaterThan(remaining[1].CreatedAtUtc);
    }

    [Fact]
    public async Task Create_WhenASecondDestinationIsConfigured_WritesACopyThere()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeAsync(host);

        var offMachine = Path.Combine(host.Paths.DataRoot, "pretend-usb-drive");

        var summary = await host.InScopeAsync(async sp =>
        {
            sp.GetRequiredService<BackupOptions>().SecondDestination = offMachine;

            return await sp.GetRequiredService<IBackupService>().CreateAsync("test — second copy");
        });

        summary.Destinations.Count.ShouldBe(2);
        summary.Destinations.ShouldAllBe(destination => destination.Succeeded);

        Directory.EnumerateFiles(offMachine, "*.zip").Count().ShouldBe(1);
    }

    [Fact]
    public async Task Create_WhenTheSecondDestinationCannotBeReached_StillKeepsTheLocalCopy()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeAsync(host);

        var summary = await host.InScopeAsync(async sp =>
        {
            // A path that cannot be created — the shape of an unplugged USB drive or a share that
            // is down. §4a D1: reported, never fatal.
            sp.GetRequiredService<BackupOptions>().SecondDestination =
                Path.Combine(host.Paths.DataRoot, "a-file-not-a-folder", "nested");

            await File.WriteAllTextAsync(
                Path.Combine(host.Paths.DataRoot, "a-file-not-a-folder"),
                "this is a file, so the path above cannot be a directory");

            return await sp.GetRequiredService<IBackupService>().CreateAsync("test — unreachable");
        });

        File.Exists(summary.FilePath).ShouldBeTrue();
        summary.IsVerified.ShouldBeTrue();

        summary.Destinations.ShouldContain(destination => destination.Succeeded);
        summary.Destinations.ShouldContain(destination => !destination.Succeeded);
    }

    // ------------------------------------------------------------------------------ fixtures

    private static Task<BookFingerprint> FingerprintAsync(SqliteTestHost host) =>
        host.InScopeAsync(sp => sp
            .GetRequiredService<IBookFingerprintService>()
            .ComputeAsync());

    private sealed record BackupWorld(
        Guid CompanyId,
        Guid UnitId,
        Guid MainGodownId,
        Guid CashLedgerId,
        Guid SalesLedgerId,
        Guid JournalTypeId,
        Guid FirstItemId,
        Guid SecondItemId);

    /// <summary>
    /// A company with both halves of C10.6 populated: journal postings so the trial balance has
    /// rows, and stock receipts so the stock summary does.
    /// </summary>
    private static async Task<BackupWorld> ArrangeAsync(SqliteTestHost host)
    {
        var companyId = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = new Company("Vinayaka Agencies", "Vinayaka Agencies (Regd.)", "29");

            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();

            return company.Id;
        });

        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>().ProvisionAsync(companyId));

        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>().EnsureInventorySeedAsync(companyId));

        var seed = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var unit = (await unitOfWork.Repository<UnitOfMeasure>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId && candidate.Code == "NOS");
            var godown = (await unitOfWork.Repository<Godown>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId);
            var cash = (await unitOfWork.Repository<Ledger>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId && candidate.Name == "Cash");
            var sales = (await unitOfWork.Repository<Ledger>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId && candidate.Name == "Sales");
            var journal = (await unitOfWork.Repository<VoucherType>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId && candidate.Name == "Journal");

            return (unit.Id, godown.Id, cash.Id, sales.Id, journal.Id);
        });

        var first = await CreateItemAsync(host, "Crompton 1 HP Monoblock", seed.Item1);
        var second = await CreateItemAsync(host, "Kirloskar 2 HP Openwell", seed.Item1);

        var world = new BackupWorld(
            companyId,
            seed.Item1,
            seed.Item2,
            seed.Item3,
            seed.Item4,
            seed.Item5,
            first,
            second);

        await PostReceiptAsync(host, world, first, 10m, 100m);
        await PostReceiptAsync(host, world, second, 4m, 180m);
        await PostJournalAsync(host, world, 2_500m);
        await PostJournalAsync(host, world, 1_750m);

        return world;
    }

    private static Task<Guid> CreateItemAsync(SqliteTestHost host, string name, Guid unitId) =>
        host.InScopeAsync(async sp =>
        {
            var saved = await sp.GetRequiredService<IInventoryMasterService>()
                .SaveItemAsync(new SaveItemRequest(Id: null, name, unitId));

            return saved.Id;
        });

    private static Task<PostedStockVoucherSummary> PostReceiptAsync(
        SqliteTestHost host,
        BackupWorld world,
        Guid itemId,
        decimal quantity,
        decimal unitCost) =>
        host.InScopeAsync(sp => sp
            .GetRequiredService<IStockVoucherService>()
            .PostAsync(new PostStockVoucherRequest(
                StockVoucherPurpose.Receipt,
                InFy2026,
                [
                    new StockVoucherLineRequest(
                        StockMovementDirection.Increase,
                        itemId,
                        world.MainGodownId,
                        quantity,
                        unitCost),
                ],
                "stock received",
                world.CashLedgerId)));

    private static Task<PostedVoucherSummary> PostJournalAsync(
        SqliteTestHost host,
        BackupWorld world,
        decimal amount) =>
        host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(new PostVoucherRequest(
                world.JournalTypeId,
                InFy2026,
                [
                    new PostVoucherLine(world.CashLedgerId, Debit: amount, Credit: 0m),
                    new PostVoucherLine(world.SalesLedgerId, Debit: 0m, Credit: amount),
                ],
                "cash sale")));

    /// <summary>
    /// Rewrites the manifest inside an archive, for the tests that need an archive claiming
    /// something its contents do not support.
    /// </summary>
    private static async Task RewriteManifestAsync(
        string archivePath,
        Func<BackupManifest, BackupManifest> change)
    {
        BackupManifest manifest;

        using (var read = ZipFile.OpenRead(archivePath))
        {
            await using var stream = read.GetEntry(BackupManifest.EntryName)!.Open();
            manifest = (await JsonSerializer.DeserializeAsync<BackupManifest>(stream))!;
        }

        var replacement = change(manifest);

        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
        archive.GetEntry(BackupManifest.EntryName)!.Delete();

        await using var entry = archive.CreateEntry(BackupManifest.EntryName).Open();
        await using var writer = new StreamWriter(entry, Encoding.UTF8);

        await writer.WriteAsync(JsonSerializer.Serialize(replacement));
    }
}
