using TravelMemory.Api.Features.Auth;

namespace TravelMemory.Api.Tests.Auth;

public sealed class SignInTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/trips/3f2a?tab=photos", "/trips/3f2a?tab=photos")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("trips", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example/", "/")]
    public void Only_returns_to_paths_on_this_site(string? returnUrl, string expected)
    {
        Assert.Equal(expected, SignIn.ToLocalUrl(returnUrl));
    }
}
