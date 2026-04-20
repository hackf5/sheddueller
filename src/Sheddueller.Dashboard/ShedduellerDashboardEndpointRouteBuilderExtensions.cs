#pragma warning disable IDE0130

namespace Microsoft.AspNetCore.Builder;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Sheddueller.Dashboard.Components;
using Sheddueller.Dashboard.Internal;

/// <summary>
/// Endpoint mapping extensions for the embedded Sheddueller dashboard.
/// </summary>
public static class ShedduellerDashboardEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the dashboard UI and live update hub under the supplied path using an application branch.
    /// </summary>
    public static IApplicationBuilder MapShedduellerDashboard(
        this IApplicationBuilder app,
        string path = "/sheddueller")
    {
        ArgumentNullException.ThrowIfNull(app);

        if (string.IsNullOrWhiteSpace(path) || path[0] != '/')
        {
            throw new ArgumentException("Dashboard path must be an absolute non-empty route path.", nameof(path));
        }

        app.Map(path, branch =>
        {
            if (RequiresCanonicalTrailingSlash(path))
            {
                branch.Use(async (context, next) =>
                {
                    if (!context.Request.Path.HasValue)
                    {
                        context.Response.Redirect(CreateCanonicalDashboardRoot(context, path: string.Empty));
                        return;
                    }

                    await next(context).ConfigureAwait(false);
                });
            }

            branch.UseRouting();
            branch.UseAntiforgery();
            branch.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorComponents<DashboardApp>()
                  .AddInteractiveServerRenderMode();
                endpoints.MapHub<DashboardUpdatesHub>("/live");
            });
        });

        return app;
    }

    /// <summary>
    /// Maps the dashboard UI and live update hub under the supplied path.
    /// </summary>
    public static IEndpointConventionBuilder MapShedduellerDashboard(
        this IEndpointRouteBuilder endpoints,
        string path = "/sheddueller")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        if (string.IsNullOrWhiteSpace(path) || path[0] != '/')
        {
            throw new ArgumentException("Dashboard path must be an absolute non-empty route path.", nameof(path));
        }

        var group = endpoints.MapGroup(path);
        group.MapRazorComponents<DashboardApp>()
          .AddInteractiveServerRenderMode();
        group.MapHub<DashboardUpdatesHub>("/live");

        return group;
    }

    private static bool RequiresCanonicalTrailingSlash(string path)
      => path.Length > 1 && path[^1] != '/';

    private static string CreateCanonicalDashboardRoot(HttpContext context, string path)
      => string.Concat(
        context.Request.PathBase.ToUriComponent(),
        path,
        "/",
        context.Request.QueryString.ToUriComponent());
}
