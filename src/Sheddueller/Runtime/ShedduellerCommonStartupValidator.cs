namespace Sheddueller.Runtime;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Sheddueller.Enqueueing;
using Sheddueller.Storage;

internal sealed class ShedduellerCommonStartupValidator(
    IServiceProvider serviceProvider,
    IOptions<ShedduellerOptions> options) : IShedduellerStartupValidator
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IOptions<ShedduellerOptions> _options = options;

    public ValueTask ValidateAsync(CancellationToken cancellationToken)
    {
        var value = this._options.Value;

        try
        {
            SubmissionValidator.ValidateRetryPolicy(value.DefaultRetryPolicy);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("ShedduellerOptions.DefaultRetryPolicy is invalid.", exception);
        }

        if (this._serviceProvider.GetService<IJobStore>() is null)
        {
            throw new InvalidOperationException("No Sheddueller job store provider has been registered.");
        }

        return ValueTask.CompletedTask;
    }
}
