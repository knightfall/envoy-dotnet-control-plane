using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Envoy.ControlPlane.AspNetCore.Tests.Support;

internal sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? authorization = Request.Headers.Authorization;
        if (authorization is not ("Bearer allowed" or "Bearer denied"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "client")], Scheme.Name);
        if (authorization == "Bearer allowed")
        {
            identity.AddClaim(new Claim("xds-node", "node"));
        }
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
