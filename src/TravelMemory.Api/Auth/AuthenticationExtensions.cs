using System.Security.Claims;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace TravelMemory.Api.Auth;

internal static class AuthenticationExtensions
{
    public const string DataProtectionContainer = "data-protection";

    // The API is a backend for the SPA it serves: it runs the OIDC flow itself and gives the
    // browser only an HttpOnly cookie, so no token is ever readable by JavaScript.
    public static void AddTravelMemoryAuthentication(this WebApplicationBuilder builder)
    {
        var oidc = builder.Configuration.GetSection("Authentication:Oidc");
        var authority = oidc["Authority"]
            ?? throw new InvalidOperationException("Authentication:Oidc:Authority is not configured.");
        var clientId = oidc["ClientId"]
            ?? throw new InvalidOperationException("Authentication:Oidc:ClientId is not configured.");
        var clientSecret = oidc["ClientSecret"]
            ?? throw new InvalidOperationException("Authentication:Oidc:ClientSecret is not configured.");

        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "TravelMemory.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                // Lax rather than Strict: the first request after the provider redirects back
                // is cross-site, and a Strict cookie would make the user look signed out.
                // Lax still keeps the cookie off cross-site POSTs, which covers CSRF because
                // the API sends no CORS headers.
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                options.SlidingExpiration = true;
                // The SPA calls the API with fetch, so answer with status codes and let it
                // navigate to the sign-in endpoint instead of following a redirect.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = authority;
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "name";
                // Keeps the id token in the encrypted cookie as the hint that lets the
                // provider end its session without asking the user to confirm.
                options.SaveTokens = true;
                // Under /api so the Vite development proxy forwards them like any API call.
                options.CallbackPath = "/api/auth/callback";
                options.SignedOutCallbackPath = "/api/auth/signed-out";
                options.Events.OnTokenValidated = async context =>
                {
                    var principal = context.Principal!;
                    var issuer = principal.FindFirstValue("iss")
                        ?? throw new InvalidOperationException("The id token has no issuer.");
                    var subject = principal.FindFirstValue("sub")
                        ?? throw new InvalidOperationException("The id token has no subject.");
                    var ownerId = await context.HttpContext.RequestServices
                        .GetRequiredService<UserDirectory>()
                        .GetOrCreateOwnerIdAsync(issuer, subject, context.HttpContext.RequestAborted);

                    // Stored in the cookie, so the owner id is resolved once per sign-in
                    // rather than on every request.
                    ((ClaimsIdentity)principal.Identity!).AddClaim(
                        new Claim(ClaimTypes.NameIdentifier, ownerId.ToString()));
                };
            });

        // Every endpoint reads the owner id, so a user without a valid one is rejected before
        // a handler runs instead of failing inside it with a 500. The policy forbids (403)
        // rather than challenges (401), because signing in again yields the same identity.
        builder.Services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context => HttpCurrentUser.TryGetOwnerId(context.User, out _))
                .Build());
        // The session and sign-in cookies are encrypted with Data Protection keys. In Azure
        // the API scales to zero and replicas come and go, so the keys live in Blob Storage
        // rather than in the container's file system, where every cold start would sign
        // everyone out. The container is private and encrypted at rest by Azure; wrapping the
        // keys with Key Vault too would add a resource and cost for little gain here.
        builder.Services.AddDataProtection()
            .SetApplicationName("TravelMemory")
            .PersistKeysToAzureBlobStorage(services => services
                .GetRequiredService<BlobServiceClient>()
                .GetBlobContainerClient(DataProtectionContainer)
                .GetBlobClient("keys.xml"));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
        builder.Services.AddScoped<UserDirectory>();

        if (builder.Environment.IsDevelopment())
        {
            // The Vite dev server proxies /api from another port. Trusting its forwarded
            // host and scheme makes the OIDC callback URL point at the page the user is on.
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto);
        }
    }
}
