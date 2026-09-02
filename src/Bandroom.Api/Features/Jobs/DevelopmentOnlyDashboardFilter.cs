using Hangfire.Dashboard;

namespace Bandroom.Api.Features.Jobs;

/// <summary>
/// The Hangfire dashboard is dev-only for now; production ops access gets a real
/// authorization story when it's needed.
/// </summary>
public sealed class DevelopmentOnlyDashboardFilter(bool isDevelopment) : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => isDevelopment;
}
