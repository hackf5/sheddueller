namespace Sheddueller.Worker.Tests;

using Sheddueller.Worker.Internal;

using Shouldly;

public sealed class WorkerRateLimitTimingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WaitTimeout_RatePermitBeforePollingDeadline_UsesRatePermit()
      => ShedduellerWorker.CalculateWaitTimeout(
        TimeSpan.FromSeconds(1),
        Now,
        Now.AddMilliseconds(250))
        .ShouldBe(TimeSpan.FromMilliseconds(250));

    [Fact]
    public void WaitTimeout_RatePermitAfterPollingDeadline_UsesPollingInterval()
      => ShedduellerWorker.CalculateWaitTimeout(
        TimeSpan.FromSeconds(1),
        Now,
        Now.AddSeconds(5))
        .ShouldBe(TimeSpan.FromSeconds(1));

    [Fact]
    public void WaitTimeout_RatePermitAlreadyDue_ReturnsZero()
      => ShedduellerWorker.CalculateWaitTimeout(
        TimeSpan.FromSeconds(1),
        Now,
        Now)
        .ShouldBe(TimeSpan.Zero);

    [Fact]
    public void WaitTimeout_NoRatePermit_UsesPollingInterval()
      => ShedduellerWorker.CalculateWaitTimeout(
        TimeSpan.FromSeconds(1),
        Now,
        nextClaimAtUtc: null)
        .ShouldBe(TimeSpan.FromSeconds(1));
}
