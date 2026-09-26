using System.Net;
using System.Text.RegularExpressions;

namespace TravelMemory.EndToEndTests;

// Follows redirects and keeps cookies the way a browser does on localhost. HttpClient's
// CookieContainer never sends Secure cookies over http, but browsers do for localhost, and
// the app's session and OIDC correlation cookies are Secure.
internal sealed partial class BrowserSession(Uri origin) : IDisposable
{
    private const int MaximumRedirects = 10;

    private readonly HttpClient client = new(
        new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private readonly Dictionary<string, string> cookies = [];

    public Uri Origin { get; } = origin;

    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        for (var redirect = 0; ; redirect++)
        {
            if (cookies.Count > 0)
            {
                request.Headers.Add(
                    "Cookie",
                    string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
            }

            var response = await client.SendAsync(request, cancellationToken);
            StoreCookies(response);
            if (response.Headers.Location is not { } location
                || (int)response.StatusCode is < 300 or > 399)
            {
                return response;
            }

            if (redirect == MaximumRedirects)
            {
                throw new InvalidOperationException($"Too many redirects, last to {location}.");
            }

            // Like a browser, every redirect this flow uses (302 and 303) becomes a GET.
            request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(request.RequestUri!, location));
            response.Dispose();
        }
    }

    public Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(Origin, path)), cancellationToken);

    public Task<HttpResponseMessage> PostAsync(
        string path,
        HttpContent? content,
        CancellationToken cancellationToken) =>
        SendAsync(
            new HttpRequestMessage(HttpMethod.Post, new Uri(Origin, path)) { Content = content },
            cancellationToken);

    // Signs in through the app's own sign-in endpoint and Keycloak's login form, and returns
    // the page the provider sent the browser back to.
    public async Task<HttpResponseMessage> SignInAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using var loginPage = await GetAsync("/api/auth/login?returnUrl=/trips", cancellationToken);
        loginPage.EnsureSuccessStatusCode();
        var html = await loginPage.Content.ReadAsStringAsync(cancellationToken);
        var action = LoginFormAction().Match(html);
        if (!action.Success)
        {
            throw new InvalidOperationException(
                $"No Keycloak login form at {loginPage.RequestMessage?.RequestUri}.");
        }

        var formUri = new Uri(
            loginPage.RequestMessage!.RequestUri!,
            WebUtility.HtmlDecode(action.Groups["action"].Value));
        var response = await SendAsync(
            new HttpRequestMessage(HttpMethod.Post, formUri)
            {
                Content = new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["username"] = username,
                        ["password"] = password,
                        ["credentialId"] = "",
                    }),
            },
            cancellationToken);
        return await SubmitAutoPostFormAsync(response, cancellationToken);
    }

    // The OIDC handler asks for response_mode=form_post, so the provider answers with a page
    // whose script posts the code to the callback. Without a browser, post it here instead.
    private async Task<HttpResponseMessage> SubmitAutoPostFormAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var form = AutoPostForm().Match(html);
        if (!form.Success)
        {
            return response;
        }

        var fields = HiddenInput().Matches(form.Groups["body"].Value)
            .ToDictionary(
                input => WebUtility.HtmlDecode(input.Groups["name"].Value),
                input => WebUtility.HtmlDecode(input.Groups["value"].Value));
        var target = new Uri(
            response.RequestMessage!.RequestUri!,
            WebUtility.HtmlDecode(form.Groups["action"].Value));
        response.Dispose();
        return await SendAsync(
            new HttpRequestMessage(HttpMethod.Post, target)
            {
                Content = new FormUrlEncodedContent(fields),
            },
            cancellationToken);
    }

    public void Dispose() => client.Dispose();

    // All hosts are localhost on fixed ports, and browsers ignore ports for cookies, so one
    // name-to-value map is enough here.
    private void StoreCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            return;
        }

        foreach (var header in headers)
        {
            var nameValue = header.Split(';', 2)[0].Split('=', 2);
            var name = nameValue[0].Trim();
            var value = nameValue.Length > 1 ? nameValue[1].Trim() : "";
            var expired = header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)
                || header.Contains("Max-Age=0", StringComparison.OrdinalIgnoreCase);
            if (expired || value.Length == 0)
            {
                cookies.Remove(name);
            }
            else
            {
                cookies[name] = value;
            }
        }
    }

    [GeneratedRegex("<form[^>]*id=\"kc-form-login\"[^>]*action=\"(?<action>[^\"]+)\"")]
    private static partial Regex LoginFormAction();

    [GeneratedRegex(
        "<form[^>]*method=\"post\"[^>]*action=\"(?<action>[^\"]+)\"[^>]*>(?<body>.*?)</form>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AutoPostForm();

    [GeneratedRegex(
        "<input[^>]*name=\"(?<name>[^\"]+)\"[^>]*value=\"(?<value>[^\"]*)\"",
        RegexOptions.IgnoreCase)]
    private static partial Regex HiddenInput();
}
