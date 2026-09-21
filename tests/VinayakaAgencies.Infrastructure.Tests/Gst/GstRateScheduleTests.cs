using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Gst;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Gst;

/// <summary>
/// Dated GST rate rows and what happens when a notification changes one
/// (phase-5-gst-returns.md §2, checkpoint C5.5).
/// </summary>
/// <remarks>
/// <b>Why these exist now.</b> Phase 5 shipped <c>GstRateService.AddAsync</c> with an overlap check
/// that tested <c>Covers(effectiveFrom)</c> against the open-ended row it was meant to supersede.
/// An open row covers every date after its start by definition, so the check rejected every rate
/// change — leaving the close-the-previous-row branch behind it unreachable. No Phase 5 test added
/// a second row for one code, so nothing caught it. It surfaced while building the Phase 7 TDS
/// equivalent, which was modelled on the same method; both are fixed, and this is the regression
/// that keeps the GST half fixed.
/// </remarks>
public sealed class GstRateScheduleTests
{
    private const long EighteenPercent = 180_000L;
    private const long TwelvePercent = 120_000L;

    [Fact]
    public async Task AddAsync_ForANotificationChangingTheRate_ClosesThePreviousRowAndKeepsBoth()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var codeId = await ArrangeCodeAsync(host);

        var april = new DateOnly(2026, 4, 1);
        var july = new DateOnly(2026, 7, 1);

        await AddRateAsync(host, codeId, EighteenPercent, april);
        await AddRateAsync(host, codeId, TwelvePercent, july);

        var schedules = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IGstRateService>().ListAsync(codeId));

        schedules.Count.ShouldBe(2, "history is closed, never rewritten");
        schedules.Single(row => row.RatePercentMinor == EighteenPercent).EffectiveTo
            .ShouldBe(new DateOnly(2026, 6, 30));
        schedules.Single(row => row.RatePercentMinor == TwelvePercent).EffectiveTo.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveAsync_PricesEachInvoiceByTheRowCoveringItsOwnDate()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var codeId = await ArrangeCodeAsync(host);

        var april = new DateOnly(2026, 4, 1);
        var july = new DateOnly(2026, 7, 1);

        await AddRateAsync(host, codeId, EighteenPercent, april);
        await AddRateAsync(host, codeId, TwelvePercent, july);

        var inJune = await ResolveAsync(host, codeId, new DateOnly(2026, 6, 30));
        var onTheDay = await ResolveAsync(host, codeId, july);
        var inAugust = await ResolveAsync(host, codeId, new DateOnly(2026, 8, 15));

        inJune.RatePercentMinor.ShouldBe(EighteenPercent);
        onTheDay.RatePercentMinor.ShouldBe(TwelvePercent, "a change applies from its own first day");
        inAugust.RatePercentMinor.ShouldBe(TwelvePercent);
    }

    [Fact]
    public async Task AddAsync_ForADateAlreadyCoveredByAClosedRow_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var codeId = await ArrangeCodeAsync(host);

        await AddRateAsync(host, codeId, EighteenPercent, new DateOnly(2026, 4, 1));
        await AddRateAsync(host, codeId, TwelvePercent, new DateOnly(2026, 7, 1));

        // 1 May falls inside the closed April–June row. Backdating into settled history would make
        // two schedules cover one date, and every return built from it ambiguous.
        await Should.ThrowAsync<InvalidOperationException>(() =>
            AddRateAsync(host, codeId, EighteenPercent, new DateOnly(2026, 5, 1)));
    }

    private static async Task<Guid> ArrangeCodeAsync(SqliteTestHost host)
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
            sp.GetRequiredService<ICompanyProvisioningService>().EnsureGstSeedAsync(companyId));

        return await host.InScopeAsync(async sp =>
            (await sp.GetRequiredService<IHsnSacService>()
                .SaveAsync(new SaveHsnSacRequest(null, "84137010", "Centrifugal pumps")))
            .Id);
    }

    private static Task<GstRateScheduleSummary> AddRateAsync(
        SqliteTestHost host,
        Guid codeId,
        long ratePercentMinor,
        DateOnly effectiveFrom) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IGstRateService>()
            .AddAsync(new SaveGstRateScheduleRequest(codeId, ratePercentMinor, effectiveFrom)));

    private static Task<ResolvedGstRate> ResolveAsync(
        SqliteTestHost host,
        Guid codeId,
        DateOnly onDate) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IGstRateService>().ResolveAsync(codeId, onDate));
}
