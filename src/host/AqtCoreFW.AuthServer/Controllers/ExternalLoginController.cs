using System.Security.Claims;
using AqtCoreFW.AuthServer.Services;
using AqtCoreFW.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.AuthServer.Controllers;

/// <summary>
/// Handles external SSO login challenge and callback with JIT provisioning.
/// </summary>
public sealed class ExternalLoginController(
    SignInManager<ApplicationUser> signInManager,
    JitProvisioningService jitService) : Controller
{
    /// <summary>
    /// Initiates external auth challenge — redirects user to external IdP.
    /// </summary>
    [HttpGet("/account/external-login")]
    public IActionResult ExternalLogin(string provider, string? returnUrl)
    {
        var callbackUrl = Url.Action(nameof(ExternalLoginCallback), new { returnUrl });
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, callbackUrl);
        return Challenge(properties, provider);
    }

    /// <summary>
    /// Callback from external IdP after user authenticates.
    /// Extracts claims from IdP response, delegates to JitProvisioningService for
    /// user lookup/creation, then signs in with Identity cookie.
    /// </summary>
    [HttpGet("/account/external-login-callback")]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl)
    {
        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
            return RedirectToLogin("external_auth_failed", returnUrl);

        // Extract claims from external IdP
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        var name = info.Principal.FindFirstValue(ClaimTypes.Name)
                   ?? info.Principal.FindFirstValue("name");

        var result = await jitService.ProvisionOrLinkAsync(
            info.LoginProvider, info.ProviderKey, email, name);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                "pending_approval" => View("~/Views/Account/PendingApproval.cshtml"),
                "deactivated" => RedirectToLogin("account_deactivated", returnUrl),
                "domain_not_allowed" => RedirectToLogin("domain_not_allowed", returnUrl),
                _ => RedirectToLogin("provision_failed", returnUrl),
            };
        }

        // Sign in the JIT-provisioned or existing linked user
        await signInManager.SignInAsync(result.User!, isPersistent: false);
        return LocalRedirect(returnUrl ?? "/");
    }

    private IActionResult RedirectToLogin(string error, string? returnUrl) =>
        RedirectToAction("Login", "Authorization", new { error, returnUrl });
}
