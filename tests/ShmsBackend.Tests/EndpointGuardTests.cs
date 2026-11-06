using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace ShmsBackend.Tests;

/// <summary>
/// Guards against an endpoint being added without a decision about who may call it. The API already denies
/// anonymous callers by default (fallback policy); these tests make the exceptions visible and reviewed.
/// </summary>
public class EndpointGuardTests
{
    private static IEnumerable<(Type Controller, MethodInfo Action)> Actions()
    {
        var controllers = typeof(ShmsBackend.Api.Controllers.PaymentController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);
        foreach (var c in controllers)
            foreach (var m in c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (m.GetCustomAttributes<HttpMethodAttribute>(true).Any())
                    yield return (c, m);
    }

    private static bool HasAllowAnonymous(Type c, MethodInfo m)
        => m.GetCustomAttributes<AllowAnonymousAttribute>(true).Any();

    [Fact]
    public void No_controller_is_allow_anonymous_at_class_level()
    {
        // A class-level [AllowAnonymous] silently overrides every method-level [Authorize] on that controller.
        var offenders = Actions().Select(a => a.Controller).Distinct()
            .Where(c => c.GetCustomAttributes<AllowAnonymousAttribute>(false).Any())
            .Select(c => c.Name).ToList();
        Assert.True(offenders.Count == 0, "Class-level [AllowAnonymous]: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Public_endpoints_are_exactly_the_reviewed_list()
    {
        var actual = Actions().Where(a => HasAllowAnonymous(a.Controller, a.Action))
            .Select(a => $"{a.Controller.Name}.{a.Action.Name}").OrderBy(x => x).ToList();

        // Review every change to this list. Anything added here is reachable without logging in.
        var allowedControllers = new[]
        {
            "AuthController", "PortalAuthController", "EmailVerificationController", "HealthController",
            "CompanySettingsController", "SecureChannelController", "PublicListingController",
            "PublicHouseTypeController", "PushConfigController", "PaymentController"
        };
        var unexpected = actual.Where(a => !allowedControllers.Any(c => a.StartsWith(c + "."))).ToList();
        Assert.True(unexpected.Count == 0,
            "Unreviewed anonymous endpoints (add [Authorize] or, if truly public, add the controller to this test): "
            + string.Join(", ", unexpected));
    }

    [Fact]
    public void Staff_money_and_personal_data_endpoints_name_their_roles()
    {
        // Endpoints in these controllers must say which roles may call them, not just "any signed-in user".
        var strict = new[] { "TenantController", "LandlordController", "AgentController", "AdminController" };
        var loose = Actions()
            .Where(a => strict.Contains(a.Controller.Name) && !HasAllowAnonymous(a.Controller, a.Action))
            .Where(a =>
            {
                var attrs = a.Action.GetCustomAttributes<AuthorizeAttribute>(true)
                    .Concat(a.Controller.GetCustomAttributes<AuthorizeAttribute>(true));
                return !attrs.Any(x => !string.IsNullOrWhiteSpace(x.Roles) || !string.IsNullOrWhiteSpace(x.Policy));
            })
            .Select(a => $"{a.Controller.Name}.{a.Action.Name}").ToList();
        Assert.True(loose.Count == 0, "No role/policy named on: " + string.Join(", ", loose));
    }
}
