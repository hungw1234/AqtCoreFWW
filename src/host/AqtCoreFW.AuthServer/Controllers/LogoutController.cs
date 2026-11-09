using AqtCoreFW.Identity.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Server.AspNetCore;

namespace AqtCoreFW.AuthServer.Controllers;

/// <summary>
/// Handles OpenIddict end-session (logout) endpoint.
/// Signs out the user and redirects to post_logout_redirect_uri.
/// </summary>
public class LogoutController(SignInManager<ApplicationUser> signInManager) : Controller
{
    [HttpGet("~/connect/logout"), HttpPost("~/connect/logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();

        // Return SignOut result to notify OpenIddict — triggers post_logout_redirect_uri redirect
        return SignOut(
            authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            properties: new AuthenticationProperties { RedirectUri = "/" });
    }
}
