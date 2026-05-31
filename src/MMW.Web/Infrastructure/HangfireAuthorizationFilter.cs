using Hangfire.Dashboard;

namespace MMW.Web.Infrastructure;

/// <summary>Chỉ cho user đã đăng nhập xem Hangfire dashboard (/hangfire).</summary>
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true;
    }
}
