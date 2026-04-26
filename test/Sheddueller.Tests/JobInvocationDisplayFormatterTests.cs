namespace Sheddueller.Tests;

using Sheddueller.Inspection.Jobs;
using Sheddueller.Storage;

using Shouldly;

public sealed class JobInvocationDisplayFormatterTests
{
    [Fact]
    public void Format_TwoArguments_KeepsCallOnSingleLine()
    {
        var call = JobInvocationDisplayFormatter.Format(
          typeof(NestedService).AssemblyQualifiedName!,
          "Run",
          [
              new JobInvocationParameterInspection(
                0,
                typeof(string).AssemblyQualifiedName!,
                new JobMethodParameterBinding(JobMethodParameterBindingKind.Serialized),
                "\"alpha\""),
              new JobInvocationParameterInspection(
                1,
                typeof(IJobContext).AssemblyQualifiedName!,
                new JobMethodParameterBinding(JobMethodParameterBindingKind.JobContext)),
          ]);

        call.ShouldBe("NestedService.Run(\"alpha\", Job.Context)");
    }

    [Fact]
    public void Format_ThreeArguments_UsesMultilineLayout()
    {
        var call = JobInvocationDisplayFormatter.Format(
          typeof(NestedService).AssemblyQualifiedName!,
          "Run",
          [
              new JobInvocationParameterInspection(
                0,
                typeof(string).AssemblyQualifiedName!,
                new JobMethodParameterBinding(JobMethodParameterBindingKind.Serialized),
                "\"alpha\""),
              new JobInvocationParameterInspection(
                1,
                typeof(IJobContext).AssemblyQualifiedName!,
                new JobMethodParameterBinding(JobMethodParameterBindingKind.JobContext)),
              new JobInvocationParameterInspection(
                2,
                typeof(CancellationToken).AssemblyQualifiedName!,
                new JobMethodParameterBinding(JobMethodParameterBindingKind.CancellationToken)),
          ]);

        call.ShouldBe(string.Join(
          Environment.NewLine,
          "NestedService.Run(",
          "    \"alpha\",",
          "    Job.Context,",
          "    CancellationToken)"));
    }

    private sealed class NestedService
    {
    }
}
