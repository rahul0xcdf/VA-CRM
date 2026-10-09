using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Accounting;

/// <summary>
/// Checkpoint C1.14 — "materialised paths agree with <c>ParentGroupId</c>" (spec §4.2, §14).
/// </summary>
/// <remarks>
/// <see cref="AccountGroup.MaterialisedPath"/> is a rebuildable cache of the ancestor chain, kept
/// so a balance roll-up is one indexed range scan instead of a recursive CTE. The whole arrangement
/// is only safe while the cache agrees with <see cref="AccountGroup.ParentGroupId"/>, which is
/// authoritative — a subtree left with stale paths reads its balances from the branch it used to be
/// under.
/// <para>
/// This criterion had no test because it had nothing to test: <c>MoveTo</c> and
/// <c>SetMaterialisedPath</c> each documented that the caller must rebuild the affected subtree,
/// and <b>no caller existed</b> — no application service re-parented a group at all. The obligation
/// was a comment. <c>IChartOfAccountsService.MoveGroupAsync</c> now discharges it, and these tests
/// are what hold it to that.
/// </para>
/// <para>
/// The recomputation below walks <c>ParentGroupId</c> and nothing else, so it cannot agree with a
/// wrong path by reusing the same code: it is the independent second opinion the criterion asks
/// for.
/// </para>
/// </remarks>
public sealed class MaterialisedPathTests
{
    private const int Seed = 20261005;

    [Fact]
    public async Task EveryPath_InARandomisedTree_EqualsThePathRecomputedFromParentIdAlone()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await ArrangeCompanyAsync(host);

        var created = await BuildRandomTreeAsync(host, companyId, nodes: 40);

        created.Count.ShouldBe(40);
        await AssertPathsAgreeWithParentIdsAsync(host, companyId);

        // The tree must actually be deep, or the assertion is only testing root children.
        var groups = await LoadGroupsAsync(host, companyId);
        groups.Max(group => group.Depth).ShouldBeGreaterThan(2);
    }

    [Fact]
    public async Task EveryPath_AfterRepeatedReparenting_StillEqualsThePathRecomputedFromParentIdAlone()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await ArrangeCompanyAsync(host);

        await BuildRandomTreeAsync(host, companyId, nodes: 40);

        var random = new Random(Seed);
        var moves = 0;

        // Twenty-five attempted moves between randomly chosen non-system groups. Many are refused
        // — a destination inside the subtree, a nature mismatch, a name clash, too deep — and a
        // refusal must leave the paths just as consistent as a success, which is why the assertion
        // runs after every iteration rather than only at the end.
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var groups = await LoadGroupsAsync(host, companyId);

            var movable = groups.Where(group => !group.IsSystem).ToList();
            var destinations = groups.ToList();

            var subject = movable[random.Next(movable.Count)];
            var destination = destinations[random.Next(destinations.Count)];

            try
            {
                await host.InScopeAsync(sp => sp
                    .GetRequiredService<IChartOfAccountsService>()
                    .MoveGroupAsync(subject.Id, destination.Id));

                moves++;
            }
            catch (InvalidOperationException)
            {
                // A refused move is a valid outcome of this fixture, not a failure of it.
            }

            await AssertPathsAgreeWithParentIdsAsync(host, companyId);
        }

        moves.ShouldBeGreaterThan(0, "the fixture must actually have moved something");
    }

    [Fact]
    public async Task MoveGroup_ToADescendantOfItself_IsRefusedAndChangesNothing()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await ArrangeCompanyAsync(host);

        var assets = await GroupIdAsync(host, companyId, "Current Assets");

        var parent = await CreateGroupAsync(host, "Depot Assets", assets);
        var child = await CreateGroupAsync(host, "Depot Fittings", parent);
        var grandchild = await CreateGroupAsync(host, "Depot Shelving", child);

        var before = await LoadGroupsAsync(host, companyId);

        var refusal = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IChartOfAccountsService>()
                .MoveGroupAsync(parent, grandchild)));

        refusal.Message.ShouldContain("sits inside");

        // Detaching a subtree is the one failure a path rebuild cannot recover from, so nothing
        // may have moved.
        var after = await LoadGroupsAsync(host, companyId);

        after.Select(group => (group.Id, group.ParentGroupId, group.MaterialisedPath))
            .ShouldBe(before.Select(group => (group.Id, group.ParentGroupId, group.MaterialisedPath)));
    }

    [Fact]
    public async Task MoveGroup_AcrossNatures_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await ArrangeCompanyAsync(host);

        var assetGroup = await CreateGroupAsync(
            host, "Depot Assets", await GroupIdAsync(host, companyId, "Current Assets"));

        var expenseParent = await GroupIdAsync(host, companyId, "Indirect Expenses");

        var refusal = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IChartOfAccountsService>()
                .MoveGroupAsync(assetGroup, expenseParent)));

        // An Asset group re-parented under Expense would move its ledgers' balances off the
        // balance sheet and onto the P&L without a voucher being touched.
        refusal.Message.ShouldContain("across natures");
    }

    [Fact]
    public async Task MoveGroup_ASystemGroup_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await ArrangeCompanyAsync(host);

        var bankAccounts = await GroupIdAsync(host, companyId, "Bank Accounts");
        var currentAssets = await GroupIdAsync(host, companyId, "Current Assets");

        var refusal = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IChartOfAccountsService>()
                .MoveGroupAsync(bankAccounts, currentAssets)));

        // The seeded positions are what make the Phase 8 Tally export map one-to-one.
        refusal.Message.ShouldContain("system group");
    }

    [Fact]
    public async Task MoveGroup_RewritesTheWholeSubtreeNotJustTheMovedNode()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await ArrangeCompanyAsync(host);

        var currentAssets = await GroupIdAsync(host, companyId, "Current Assets");

        var origin = await CreateGroupAsync(host, "Depot A", currentAssets);
        var destination = await CreateGroupAsync(host, "Depot B", currentAssets);
        var moved = await CreateGroupAsync(host, "Spares", origin);
        var deep = await CreateGroupAsync(host, "Bearings", moved);
        var deeper = await CreateGroupAsync(host, "Sealed Bearings", deep);

        await host.InScopeAsync(sp => sp
            .GetRequiredService<IChartOfAccountsService>()
            .MoveGroupAsync(moved, destination));

        var groups = (await LoadGroupsAsync(host, companyId)).ToDictionary(group => group.Id);

        // This is the assertion that fails if only the moved node is rebuilt — the failure mode
        // the two domain comments warn about and nothing previously prevented.
        groups[deeper].MaterialisedPath
            .ShouldStartWith(groups[destination].MaterialisedPath);
        groups[deeper].MaterialisedPath
            .ShouldContain($"{moved:N}");
        groups[deeper].MaterialisedPath
            .ShouldNotContain($"{origin:N}", Case.Sensitive);

        groups[deep].MaterialisedPath.ShouldStartWith(groups[moved].MaterialisedPath);
        groups[deeper].Depth.ShouldBe(groups[destination].Depth + 3);

        await AssertPathsAgreeWithParentIdsAsync(host, companyId);
    }

    // ------------------------------------------------------------------------------- helpers

    /// <summary>
    /// The independent recomputation: walk <c>ParentGroupId</c> to the root and compose the path
    /// from the ids alone, touching nothing the production code wrote.
    /// </summary>
    private static async Task AssertPathsAgreeWithParentIdsAsync(SqliteTestHost host, Guid companyId)
    {
        var groups = await LoadGroupsAsync(host, companyId);
        var byId = groups.ToDictionary(group => group.Id);

        foreach (var group in groups)
        {
            var chain = new List<Guid>();

            for (var cursor = group; ; )
            {
                chain.Insert(0, cursor.Id);

                if (cursor.ParentGroupId is not { } parentId)
                {
                    break;
                }

                byId.ContainsKey(parentId).ShouldBeTrue(
                    $"'{group.Name}' has parent {parentId}, which does not exist");

                cursor = byId[parentId];

                chain.Count.ShouldBeLessThanOrEqualTo(
                    AccountGroup.MaxDepth + 1,
                    $"walking up from '{group.Name}' did not terminate — the parent chain has a cycle");
            }

            var expected = "/" + string.Join("/", chain.Select(id => id.ToString("N"))) + "/";

            group.MaterialisedPath.ShouldBe(
                expected,
                $"'{group.Name}' caches a path that disagrees with its parent chain");

            group.Depth.ShouldBe(chain.Count);
        }
    }

    private static Task<IReadOnlyList<AccountGroup>> LoadGroupsAsync(
        SqliteTestHost host,
        Guid companyId) =>
        host.InScopeAsync(async sp =>
        {
            var all = await sp.GetRequiredService<IUnitOfWork>()
                .Repository<AccountGroup>()
                .ListAsync();

            return (IReadOnlyList<AccountGroup>)all
                .Where(group => group.CompanyId == companyId)
                .OrderBy(group => group.MaterialisedPath, StringComparer.Ordinal)
                .ToList();
        });

    /// <summary>
    /// Forty groups hung at random off the seeded chart, each under a parent of its own nature so
    /// the tree is legal, and biased towards whatever was created last so it grows deep rather
    /// than wide.
    /// </summary>
    private static async Task<List<Guid>> BuildRandomTreeAsync(
        SqliteTestHost host,
        Guid companyId,
        int nodes)
    {
        var random = new Random(Seed);
        var created = new List<Guid>();

        var seeded = await LoadGroupsAsync(host, companyId);
        var candidates = seeded.ToDictionary(group => group.Id, group => group.Depth);

        for (var index = 0; index < nodes; index++)
        {
            // Pick a parent shallow enough to leave room, favouring recent nodes for depth.
            var open = candidates.Where(pair => pair.Value < AccountGroup.MaxDepth - 1).ToList();
            var parentId = random.Next(3) == 0
                ? open[random.Next(open.Count)].Key
                : open[^(1 + random.Next(Math.Min(5, open.Count)))].Key;

            var id = await CreateGroupAsync(host, $"Generated {index + 1:D2}", parentId);

            created.Add(id);
            candidates[id] = candidates[parentId] + 1;
        }

        return created;
    }

    private static Task<Guid> CreateGroupAsync(SqliteTestHost host, string name, Guid parentId) =>
        host.InScopeAsync(async sp =>
        {
            var summary = await sp.GetRequiredService<IChartOfAccountsService>()
                .CreateGroupAsync(new CreateAccountGroupRequest(name, parentId));

            return summary.Id;
        });

    private static async Task<Guid> GroupIdAsync(SqliteTestHost host, Guid companyId, string name) =>
        (await LoadGroupsAsync(host, companyId)).Single(group => group.Name == name).Id;

    private static async Task<Guid> ArrangeCompanyAsync(SqliteTestHost host)
    {
        var companyId = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = new Company("Vinayaka Agencies", "Vinayaka Agencies (Regd.)", "29");

            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();

            return company.Id;
        });

        await host.InScopeAsync(sp => sp
            .GetRequiredService<ICompanyProvisioningService>()
            .ProvisionAsync(companyId));

        return companyId;
    }
}
