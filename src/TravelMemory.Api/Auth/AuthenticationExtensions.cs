using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace TravelMemory.Api.Auth;

internal static class AuthenticationExtensions
{
    public static void AddTravelMemoryAuthentication(this WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "A production authentication provider must be configured before running outside Development.");
        }

        var configuredOwnerId = builder.Configuration["Identity:DevelopmentOwnerId"];
        if (!Guid.TryParse(configuredOwnerId, out var ownerId) || ownerId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Identity:DevelopmentOwnerId must contain a non-empty GUID in Development.");
        }

        builder.Services.Configure<DevelopmentIdentityOptions>(options => options.OwnerId = ownerId);
        builder.Services
            .AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
                DevelopmentAuthenticationHandler.SchemeName,
                _ => { });
        // Every endpoint reads the owner id, so a user without a valid one is rejected before
        // a handler runs instead of failing inside it with a 500. The policy forbids (403)
        // rather than challenges (401), because signing in again yields the same identity.
        builder.Services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context => HttpCurrentUser.TryGetOwnerId(context.User, out _))
                .Build());
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
    }
}
