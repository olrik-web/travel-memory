using TravelMemory.Api.Features.Auth;

namespace TravelMemory.IntegrationTests.Auth;

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
    [InlineData("/\t/evil.example/", "/")]
    [InlineData("/\n/evil.example/", "/")]
    [InlineData("/\r/evil.example/", "/")]
    [InlineData("/trips\t", "/")]
    public void Only_returns_to_paths_on_this_site(string? returnUrl, string expected)
    {
        Assert.Equal(expected, SignIn.ToLocalUrl(returnUrl));
    }
}
