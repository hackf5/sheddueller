namespace Sheddueller.Dashboard.Tests;

using Sheddueller.Dashboard.Internal;

using Shouldly;

public sealed class DashboardJobColumnWidthTests
{
    [Fact]
    public void Widths_DefaultsAndOverrides_CalculateExactTableWidth()
    {
        var columns = new[]
        {
            new ShedduellerDashboardJobColumn(ShedduellerDashboardJobColumnKind.JobId),
            new ShedduellerDashboardJobColumn(ShedduellerDashboardJobColumnKind.Handler) { Width = 300 },
            new ShedduellerDashboardJobColumn(ShedduellerDashboardJobColumnKind.Attempts),
        };

        DashboardJobColumnWidths.Get(columns[0]).ShouldBe(64);
        DashboardJobColumnWidths.Get(columns[1]).ShouldBe(300);
        DashboardJobColumnWidths.Total(columns).ShouldBe(420);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(48, true)]
    [InlineData(800, true)]
    [InlineData(47, false)]
    [InlineData(801, false)]
    public void Widths_Validation_EnforcesSupportedOverrides(
        int? width,
        bool expected)
      => DashboardJobColumnWidths.IsValid(width).ShouldBe(expected);

    [Fact]
    public void Widths_SetAndReset_NormalizesDefaultsAndPreservesOtherColumnMetadata()
    {
        var columns = new List<ShedduellerDashboardJobColumn>
        {
            new(ShedduellerDashboardJobColumnKind.JobId),
            new(ShedduellerDashboardJobColumnKind.Tag, "provider", "Provider"),
        };

        DashboardJobColumnWidths.Set(columns, index: 1, width: 240).ShouldBeTrue();
        columns[1].ShouldBe(new ShedduellerDashboardJobColumn(
          ShedduellerDashboardJobColumnKind.Tag,
          "provider",
          "Provider")
        {
            Width = 240,
        });
        DashboardJobColumnWidths.Set(columns, index: 1, width: 160).ShouldBeTrue();
        columns[1].Width.ShouldBeNull();
        DashboardJobColumnWidths.Set(columns, index: 0, width: 120).ShouldBeTrue();
        DashboardJobColumnWidths.Reset(columns).ShouldBeTrue();
        columns.ShouldAllBe(column => column.Width == null);
    }

    [Fact]
    public void Widths_AutoFit_ClampsCompactAndDescriptiveKindsIndependently()
    {
        DashboardJobColumnWidths.ClampAutoFit(ShedduellerDashboardJobColumnKind.Attempts, 500).ShouldBe(96);
        DashboardJobColumnWidths.ClampAutoFit(ShedduellerDashboardJobColumnKind.Handler, 500).ShouldBe(420);
        DashboardJobColumnWidths.ClampAutoFit(ShedduellerDashboardJobColumnKind.Tag, 20).ShouldBe(72);
    }
}
