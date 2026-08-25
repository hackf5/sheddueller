namespace Sheddueller.Dashboard.Tests;

using Sheddueller.Dashboard.Internal;

using Shouldly;

public sealed class DashboardJobColumnEditorTests
{
    [Fact]
    public void Move_ValidTarget_ReordersLinearColumnList()
    {
        var columns = CreateColumns(
          ShedduellerDashboardJobColumnKind.JobId,
          ShedduellerDashboardJobColumnKind.Enqueued,
          ShedduellerDashboardJobColumnKind.State,
          ShedduellerDashboardJobColumnKind.Handler);

        DashboardJobColumnEditor.Move(columns, fromIndex: 3, toIndex: 1).ShouldBeTrue();

        columns.Select(column => column.Kind).ShouldBe(
        [
            ShedduellerDashboardJobColumnKind.JobId,
            ShedduellerDashboardJobColumnKind.Handler,
            ShedduellerDashboardJobColumnKind.Enqueued,
            ShedduellerDashboardJobColumnKind.State,
        ]);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 0)]
    [InlineData(0, 4)]
    public void Move_InvalidOrUnchangedTarget_DoesNotMutate(
        int fromIndex,
        int toIndex)
    {
        var columns = CreateColumns(
          ShedduellerDashboardJobColumnKind.JobId,
          ShedduellerDashboardJobColumnKind.State,
          ShedduellerDashboardJobColumnKind.Handler,
          ShedduellerDashboardJobColumnKind.Attempts);
        var original = columns.ToArray();

        DashboardJobColumnEditor.Move(columns, fromIndex, toIndex).ShouldBeFalse();

        columns.ShouldBe(original);
    }

    [Fact]
    public void Visibility_HideAndRestoreBuiltIn_AppendsRestoredColumnAndLocksJobId()
    {
        var columns = CreateColumns(
          ShedduellerDashboardJobColumnKind.JobId,
          ShedduellerDashboardJobColumnKind.State,
          ShedduellerDashboardJobColumnKind.Handler);

        DashboardJobColumnEditor.SetBuiltInVisibility(columns, ShedduellerDashboardJobColumnKind.State, visible: false).ShouldBeTrue();
        DashboardJobColumnEditor.SetBuiltInVisibility(columns, ShedduellerDashboardJobColumnKind.State, visible: true).ShouldBeTrue();
        DashboardJobColumnEditor.SetBuiltInVisibility(columns, ShedduellerDashboardJobColumnKind.JobId, visible: false).ShouldBeFalse();

        columns.Select(column => column.Kind).ShouldBe(
        [
            ShedduellerDashboardJobColumnKind.JobId,
            ShedduellerDashboardJobColumnKind.Handler,
            ShedduellerDashboardJobColumnKind.State,
        ]);
    }

    [Fact]
    public void PromotedTag_AddAndUpdate_NormalizesValuesAndRejectsDuplicates()
    {
        var columns = CreateColumns(ShedduellerDashboardJobColumnKind.JobId);

        DashboardJobColumnEditor.TryAddTag(columns, " provider ", " Provider ", out var addError).ShouldBeTrue();
        columns[1] = columns[1] with { Width = 240 };
        DashboardJobColumnEditor.TryAddTag(columns, "PROVIDER", null, out var duplicateError).ShouldBeFalse();
        DashboardJobColumnEditor.TryUpdateTag(columns, 1, " manager ", " ", out var updateError).ShouldBeTrue();

        addError.ShouldBeNull();
        duplicateError.ShouldBe("That tag already has a promoted column.");
        updateError.ShouldBeNull();
        columns[1].ShouldBe(new ShedduellerDashboardJobColumn(
          ShedduellerDashboardJobColumnKind.Tag,
          "manager",
          Heading: null)
        {
            Width = 240,
        });
    }

    private static List<ShedduellerDashboardJobColumn> CreateColumns(
        params ShedduellerDashboardJobColumnKind[] kinds)
      => [.. kinds.Select(kind => new ShedduellerDashboardJobColumn(kind))];
}
