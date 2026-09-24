using Microsoft.AspNetCore.Authentication;

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
        builder.Services.AddAuthorization();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
    }
}
