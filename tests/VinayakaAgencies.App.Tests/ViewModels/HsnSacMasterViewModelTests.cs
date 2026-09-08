using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.Application.Gst;

namespace VinayakaAgencies.App.Tests.ViewModels;

public sealed class HsnSacMasterViewModelTests
{
    [Fact]
    public async Task Refresh_WithScheduledRates_ShowsCurrentRateAndCessPerCode()
    {
        var codeId = Guid.NewGuid();

        var hsnSac = new FakeHsnSacService();
        hsnSac.Seed(new HsnSacSummary(codeId, "8413", "Centrifugal pumps", true));

        var rates = new FakeGstRateService();
        rates.SetRate(codeId, ratePercentMinor: 180000, cessPercentMinor: 0);

        var viewModel = new HsnSacMasterViewModel(hsnSac, rates);
        await ((IAsyncRelayCommand)viewModel.RefreshCommand).ExecuteAsync(null);

        var row = viewModel.Rows.Single();
        row.Code.ShouldBe("8413");
        row.IsActive.ShouldBeTrue();
        row.RatePercent.ShouldBe("18");
        row.CessPercent.ShouldBe("0");
    }

    [Fact]
    public async Task Save_WithEditorFilled_CreatesThroughTheServiceAndReportsIt()
    {
        var hsnSac = new FakeHsnSacService();
        var viewModel = new HsnSacMasterViewModel(hsnSac, new FakeGstRateService())
        {
            EditCode = "998314",
            EditDescription = "Site formation services",
            EditIsActive = true,
        };

        await ((IAsyncRelayCommand)viewModel.SaveCommand).ExecuteAsync(null);

        viewModel.Status.ShouldContain("Saved '998314'");
        hsnSac.Rows.ShouldContain(summary => summary.Code == "998314");
    }

    [Fact]
    public void Screen_DeclaresKeyboardShortcuts_AndRoutesAcceptToSave()
    {
        var viewModel = new HsnSacMasterViewModel(
            new FakeHsnSacService(), new FakeGstRateService());

        viewModel.StatusBarHints.ShouldBe(HsnSacMasterViewModel.ShortcutHints);
        viewModel.AcceptCommand.ShouldBe(viewModel.SaveCommand);
        viewModel.CancelCommand.ShouldBe(viewModel.ClearEditorCommand);
    }

    private sealed class FakeHsnSacService : IHsnSacService
    {
        public List<HsnSacSummary> Rows { get; } = [];

        public void Seed(HsnSacSummary row) => Rows.Add(row);

        public Task<IReadOnlyList<HsnSacSummary>> ListAsync(
            bool includeInactive = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HsnSacSummary>>(
                Rows.Where(row => includeInactive || row.IsActive).ToList());

        public Task<HsnSacSummary?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));

        public Task<HsnSacSummary> SaveAsync(SaveHsnSacRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Id is not { } id)
            {
                id = Guid.NewGuid();
                Rows.Add(new HsnSacSummary(id, request.Code, request.Description, request.IsActive));
                return Task.FromResult(Rows.Single(row => row.Id == id));
            }

            var index = Rows.FindIndex(row => row.Id == id);
            Rows[index] = new HsnSacSummary(id, request.Code, request.Description, request.IsActive);
            return Task.FromResult(Rows[index]);
        }

        public Task DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
        {
            SetActive(id, false);
            return Task.CompletedTask;
        }

        public Task ActivateAsync(Guid id, CancellationToken cancellationToken = default)
        {
            SetActive(id, true);
            return Task.CompletedTask;
        }

        private void SetActive(Guid id, bool isActive)
        {
            var index = Rows.FindIndex(row => row.Id == id);
            Rows[index] = Rows[index] with { IsActive = isActive };
        }
    }

    private sealed class FakeGstRateService : IGstRateService
    {
        private readonly Dictionary<Guid, (long Rate, long Cess)> _ratesByCodeId = [];

        public void SetRate(Guid hsnSacId, long ratePercentMinor, long cessPercentMinor) =>
            _ratesByCodeId[hsnSacId] = (ratePercentMinor, cessPercentMinor);

        public Task<IReadOnlyList<GstRateScheduleSummary>> ListAsync(
            Guid? hsnSacId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GstRateScheduleSummary>>([]);

        public Task<GstRateScheduleSummary> AddAsync(
            SaveGstRateScheduleRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<ResolvedGstRate> ResolveAsync(
            Guid hsnSacId,
            DateOnly onDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_ratesByCodeId.TryGetValue(hsnSacId, out var rate)
                ? new ResolvedGstRate(Guid.NewGuid(), hsnSacId, rate.Rate, rate.Cess)
                : ResolvedGstRate.None(hsnSacId));
    }
}
