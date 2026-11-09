using System.Security.Claims;
using AqtCoreFW.Audit.Domain.Services;
using AqtCoreFW.AuthServer.Configuration;
using AqtCoreFW.AuthServer.Services;
using AqtCoreFW.Identity.Application.Services;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Microsoft.AspNetCore;  // GetOpenIddictServerRequest extension

namespace AqtCoreFW.AuthServer.Controllers;

/// <summary>
/// Handles OpenIddict authorization + token endpoints.
/// Authorization Code + PKCE for SPA, Password (ROPC) for dev tooling.
/// </summary>
public class AuthorizationController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IAuditService auditService,
    ILockoutEscalationService lockoutEscalationService,
    ICredentialPolicyRepository credentialPolicyRepository,
    MfaTempTokenService mfaTempTokenService,
    IMfaEnrolmentEvaluator mfaEnrolmentEvaluator,
    IMfaEnrolmentSettings mfaEnrolmentSettings,
    IOptions<MfaOptions> mfaOptions,
    IPasswordVerifyTimingDecoy passwordVerifyTimingDecoy) : Controller
{
    // Claim type for MFA verification timestamp (Unix epoch seconds).
    // Emitted into cookie on MFA completion; forwarded to OpenIddict access token.
    private const string MfaAtClaimType = "mfa_at";

    // Claim type signaling FE that this user must enrol MFA before continuing (QĐ 742 1.6.b).
    // Emitted into both access_token + id_token by CreateIdentity when feature flag is on
    // and IMfaEnrolmentEvaluator says yes. FE auth-provider reads it via oidc-client user.profile.
    private const string MfaEnrolmentRequiredClaimType = "mfa_enrolment_required";

    // GET /connect/authorize — PKCE authorization code flow
    [HttpGet("/connect/authorize"), HttpPost("/connect/authorize")]
    public async Task<IActionResult> Authorize()
    {
        var result = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!result.Succeeded)
        {
            // Not logged in → redirect to login page, preserving the OIDC query string
            var returnUrl = Request.PathBase + Request.Path + Request.QueryString;
            return LocalRedirect($"/account/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var user = await userManager.GetUserAsync(result.Principal!);
        if (user is null) return Forbid();

        // Build identity with claims OpenIddict needs (including roles)
        var identity = await CreateIdentity(user);

        // Forward mfa_at from cookie (set on MFA completion) to OpenIddict access token.
        // mfa_at is stored in the Identity application cookie by VerifyMfaPost.
        // This is the only path where mfa_at enters the token; ROPC/refresh paths do NOT emit it.
        var existingMfaAt = result.Principal?.FindFirstValue(MfaAtClaimType);
        if (!string.IsNullOrEmpty(existingMfaAt))
        {
            var mfaAtClaim = new Claim(MfaAtClaimType, existingMfaAt, ClaimValueTypes.Integer64);
            mfaAtClaim.SetDestinations(Destinations.AccessToken);
            identity.AddClaim(mfaAtClaim);
        }

        // Set scopes from the OIDC request
        var request = HttpContext.GetOpenIddictServerRequest()!;
        identity.SetScopes(request.GetScopes());
        // Include API resource as audience — ensures introspection returns all claims to API
        identity.SetResources("aqtcorefw-api-resource");

        await StampMfaAtIfOptionalAsync(identity, user);

        // OpenIddict generates the auth code + handles PKCE validation
        return SignIn(new ClaimsPrincipal(identity),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // POST /connect/token — handles auth code exchange + ROPC
    [HttpPost("/connect/token")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()!;

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            // OpenIddict already validated the auth code / refresh token
            var principal = (await HttpContext.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal!;

            var userId = principal.GetClaim(Claims.Subject);
            var user = userId is not null ? await userManager.FindByIdAsync(userId) : null;
            if (user is null)
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            // Refresh claims from current user data (including roles)
            var identity = await CreateIdentity(user);
            identity.SetScopes(principal.GetScopes());
            identity.SetResources("aqtcorefw-api-resource");

            // Preserve mfa_at across token refresh so MFA freshness is not reset on refresh.
            // The claim was originally set on the auth-code flow when user completed MFA.
            var refreshMfaAt = principal.GetClaim(MfaAtClaimType);
            if (!string.IsNullOrEmpty(refreshMfaAt))
            {
                var mfaAtClaim = new Claim(MfaAtClaimType, refreshMfaAt, ClaimValueTypes.Integer64);
                mfaAtClaim.SetDestinations(Destinations.AccessToken);
                identity.AddClaim(mfaAtClaim);
            }

            await StampMfaAtIfOptionalAsync(identity, user);

            return SignIn(new ClaimsPrincipal(identity),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsPasswordGrantType())
        {
            // ROPC — dev only (gated by AllowPasswordFlow in Program.cs)
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var ua = Request.Headers.UserAgent.FirstOrDefault();
            var username = request.Username ?? "";

            var user = await userManager.FindByEmailAsync(username)
                       ?? await userManager.FindByNameAsync(username);

            if (user is null)
            {
                // OWASP A07 / QĐ742: equalize timing vs user-found path's CheckPasswordSignInAsync
                passwordVerifyTimingDecoy.Consume(request.Password ?? string.Empty);
                await auditService.LogLoginAttemptAsync(
                    null, username, ip, ua, false, "ROPC: user not found");
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            // Use SignInManager to trigger lockout counting (QĐ742 §5.2 compliance)
            var signInResult = await signInManager.CheckPasswordSignInAsync(
                user, request.Password!, lockoutOnFailure: true);

            if (signInResult.IsLockedOut)
            {
                // Trigger escalation: may auto-disable account after N consecutive lockouts (QĐ 742 1.4.d)
                var ct = HttpContext.RequestAborted;
                var escalated = await lockoutEscalationService.OnLockoutAsync(user, ct);
                await auditService.LogLoginAttemptAsync(
                    user.Id, username, ip, ua, false,
                    escalated ? "ROPC: account auto-disabled after escalation" : "ROPC: account locked out");
                // RFC 6749 §5.2: lockout = invalid_grant (not access_denied)
                return Forbid(new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] =
                        OpenIddictConstants.Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = escalated
                        ? "Account auto-disabled. Contact administrator."
                        : "Account locked. Try again later.",
                }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            if (!signInResult.Succeeded)
            {
                await auditService.LogLoginAttemptAsync(
                    user.Id, username, ip, ua, false, "ROPC: invalid credentials");
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            // QĐ 742 1.6.b — MFA challenge for ROPC flow.
            //
            // Branch A: user has TotpEnrolledAt != null (MFA enrolled) →
            //   Issue mfa_required + temp_token (5-min, single-use).
            //   FE calls POST /connect/mfa-challenge with code + temp_token.
            //
            // Branch B: mandatory policy active + grace expired + no enrolment →
            //   Hard 403 mfa_enrol_required — no temp_token (nothing to challenge against).
            //   Admin emergency disable is the only escape (QĐ742 1.6 P1-1 safeguard).
            //
            // Branch C: mandatory policy active but within grace OR no policy → pass through.
            if (user.TotpEnrolledAt.HasValue)
            {
                // Branch A: MFA enrolled — issue challenge token.
                await auditService.LogLoginAttemptAsync(
                    user.Id, username, ip, ua, false, "ROPC: mfa_required (enrolled)");
                var tempToken = mfaTempTokenService.Issue(user.Id);
                return Forbid(new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = "mfa_required",
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "Multi-factor authentication required. Use the temp_token to complete MFA.",
                    ["temp_token"] = tempToken,
                }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }
            else
            {
                // Check mandatory policy + grace expiry (Branch B) via shared evaluator (DRY with CreateIdentity claim path).
                // Note: evaluator is a pure compliance predicate — does NOT depend on Mfa:RedirectOnEnrolmentRequired
                // feature flag. ROPC's hard-block must remain unconditional regardless of FE-redirect toggle.
                var ct = HttpContext.RequestAborted;
                if (await mfaEnrolmentEvaluator.RequiresEnrolmentAsync(user, ct))
                {
                    await auditService.LogLoginAttemptAsync(
                        user.Id, username, ip, ua, false, "ROPC: mfa_enrol_required (grace expired)");
                    return Forbid(new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = "mfa_enrol_required",
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "MFA enrolment is mandatory and the grace period has expired. " +
                            "Contact your administrator.",
                    }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
                }
                // No mandatory policy, within grace, or feature path inapplicable → Branch C, normal login.
            }

            await auditService.LogLoginAttemptAsync(user.Id, username, ip, ua, true, "ROPC");

            // Reset consecutive lockout counter on successful login (QĐ 742 1.4.d)
            await lockoutEscalationService.ResetLockoutCounterAsync(user, HttpContext.RequestAborted);

            var identity = await CreateIdentity(user);
            identity.SetScopes(request.GetScopes());
            identity.SetResources("aqtcorefw-api-resource");

            await StampMfaAtIfOptionalAsync(identity, user);

            return SignIn(new ClaimsPrincipal(identity),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return BadRequest(new { error = "unsupported_grant_type" });
    }

    // GET /account/login — render login form
    [HttpGet("/account/login")]
    public async Task<IActionResult> Login(string? returnUrl, string? error)
    {
        var schemes = await signInManager.GetExternalAuthenticationSchemesAsync();
        ViewData["ExternalSchemes"] = schemes.ToList();
        if (!string.IsNullOrEmpty(error))
            ModelState.AddModelError("", error switch
            {
                "external_auth_failed" => "Xác thực SSO thất bại. Vui lòng thử lại.",
                "no_linked_account" => "Tài khoản SSO chưa được liên kết. Vui lòng liên hệ quản trị viên.",
                "account_deactivated" => "Tài khoản đã bị vô hiệu hóa.",
                "domain_not_allowed" => "Email của bạn không nằm trong danh sách domain được phép.",
                "provision_failed" => "Không thể tạo tài khoản tự động. Vui lòng liên hệ quản trị viên.",
                "jit_disabled" => "Đăng nhập SSO tự động chưa được bật cho nhà cung cấp này.",
                "rate_limited" => "Hệ thống đang quá tải. Vui lòng thử lại sau.",
                _ => "Đã xảy ra lỗi. Vui lòng thử lại."
            });
        return View("~/Views/Account/Login.cshtml", new LoginViewModel { ReturnUrl = returnUrl ?? "/" });
    }

    // POST /account/login — validate credentials, sign in with Identity cookie
    [HttpPost("/account/login")]
    public async Task<IActionResult> LoginPost(LoginViewModel model)
    {
        // Pass external schemes to view for SSO buttons on validation error
        var externalSchemes = await signInManager.GetExternalAuthenticationSchemesAsync();
        ViewData["ExternalSchemes"] = externalSchemes.ToList();

        if (!ModelState.IsValid)
            return View("~/Views/Account/Login.cshtml", model);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ua = Request.Headers.UserAgent.FirstOrDefault();

        var user = await userManager.FindByEmailAsync(model.Email);
        if (user is null)
        {
            // OWASP A07 / QĐ742: equalize timing vs user-found path's PasswordSignInAsync
            passwordVerifyTimingDecoy.Consume(model.Password);
            await auditService.LogLoginAttemptAsync(null, model.Email, ip, ua, false, "User not found");
            // OWASP A04 anti-enumeration: identical message to user-found wrong-password branch (L318/323)
            ModelState.AddModelError("", "Sai tên đăng nhập hoặc mật khẩu.");
            return View("~/Views/Account/Login.cshtml", model);
        }

        var result = await signInManager.PasswordSignInAsync(
            user, model.Password, isPersistent: false, lockoutOnFailure: true);

        if (result.RequiresTwoFactor)
        {
            // MFA enabled — redirect to verification page, preserving return URL
            TempData["ReturnUrl"] = model.ReturnUrl;
            return RedirectToAction("VerifyMfa");
        }

        if (result.IsLockedOut)
        {
            // Trigger escalation: may auto-disable account after N consecutive lockouts (QĐ 742 1.4.d)
            var escalated = await lockoutEscalationService.OnLockoutAsync(user, HttpContext.RequestAborted);
            await auditService.LogLoginAttemptAsync(
                user.Id, model.Email, ip, ua, false,
                escalated ? "Account auto-disabled after escalation" : "Account locked out");
            ModelState.AddModelError(string.Empty, escalated
                ? "Tài khoản đã bị tự động khóa do nhiều lần sai mật khẩu liên tiếp. Liên hệ quản trị viên."
                : "Tài khoản đã bị khóa tạm thời. Vui lòng thử lại sau.");
            return View("~/Views/Account/Login.cshtml", model);
        }

        if (!result.Succeeded)
        {
            await auditService.LogLoginAttemptAsync(user.Id, model.Email, ip, ua, false, "Invalid password");

            // Augment error message with remaining attempts when policy requests it (QĐ 742 1.4.b)
            var policy = await credentialPolicyRepository
                .GetDefaultForTenantAsync(user.TenantId ?? Guid.Empty, HttpContext.RequestAborted);
            if (policy?.ShowRemainingAttempts == true)
            {
                var maxAttempts = policy.MaxFailedAttempts > 0 ? policy.MaxFailedAttempts : 5;
                var remaining = Math.Max(0, maxAttempts - user.AccessFailedCount);
                if (remaining is > 0 and <= 2)
                {
                    ModelState.AddModelError(string.Empty,
                        $"Sai tên đăng nhập hoặc mật khẩu. Còn lại {remaining} lần thử trước khi tài khoản bị khóa.");
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Sai tên đăng nhập hoặc mật khẩu.");
                }
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Sai tên đăng nhập hoặc mật khẩu.");
            }

            return View("~/Views/Account/Login.cshtml", model);
        }

        // Successful login — reset consecutive lockout counter (QĐ 742 1.4.d)
        await lockoutEscalationService.ResetLockoutCounterAsync(user, HttpContext.RequestAborted);
        await auditService.LogLoginAttemptAsync(user.Id, model.Email, ip, ua, true);

        // Redirect back to /connect/authorize with original OIDC params
        return LocalRedirect(model.ReturnUrl ?? "/");
    }

    // GET /account/verify-mfa — render MFA verification form
    [HttpGet("/account/verify-mfa")]
    public IActionResult VerifyMfa() =>
        View("~/Views/Account/VerifyMfa.cshtml", new VerifyMfaViewModel());

    // POST /account/verify-mfa — validate TOTP code
    [HttpPost("/account/verify-mfa")]
    public async Task<IActionResult> VerifyMfaPost(VerifyMfaViewModel model)
    {
        if (!ModelState.IsValid)
            return View("~/Views/Account/VerifyMfa.cshtml", model);

        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(
            model.Code, isPersistent: false, rememberClient: false);

        if (result.Succeeded)
        {
            // Stamp mfa_at into the Identity application cookie so it flows into the access token
            // when /connect/authorize calls CreateIdentity on the next request.
            //
            // MUST use HttpContext.User here — NOT AuthenticateAsync.
            // SignInManager.TwoFactorAuthenticatorSignInAsync calls Context.SignInAsync which updates
            // HttpContext.User to the newly-signed-in principal. AuthenticateAsync caches its result
            // per-request and returns stale "no auth" if the middleware already ran before MFA.
            if (HttpContext.User?.Identity is ClaimsIdentity cookieIdentity
                && cookieIdentity.IsAuthenticated)
            {
                var mfaAtValue = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                cookieIdentity.AddClaim(new Claim(MfaAtClaimType, mfaAtValue, ClaimValueTypes.Integer64));
                // Re-issue cookie so mfa_at persists to the next HTTP request (/connect/authorize).
                await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, HttpContext.User);
            }

            var returnUrl = TempData["ReturnUrl"]?.ToString() ?? "/";
            return LocalRedirect(returnUrl);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError("", "Tài khoản đã bị khóa tạm thời. Vui lòng thử lại sau.");
            return View("~/Views/Account/VerifyMfa.cshtml", model);
        }

        ModelState.AddModelError("", "Mã xác thực không hợp lệ.");
        return View("~/Views/Account/VerifyMfa.cshtml", model);
    }

    /// <summary>
    /// QĐ742 1.6.b — when SystemParameter `mfa.enrolment.required` resolves to false for the
    /// user's tenant, stamp `mfa_at = now` so [RequiresMfa] step-up checks succeed for
    /// non-enrolled users. Preserves real mfa_at when present (cookie/refresh paths).
    /// </summary>
    private async Task StampMfaAtIfOptionalAsync(ClaimsIdentity identity, ApplicationUser user)
    {
        if (identity.HasClaim(c => c.Type == MfaAtClaimType)) return;

        var enrolmentRequired = await mfaEnrolmentSettings.IsEnrolmentRequiredAsync(
            user.TenantId?.ToString(), HttpContext.RequestAborted);
        if (enrolmentRequired) return;

        var stamped = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var claim = new Claim(MfaAtClaimType, stamped, ClaimValueTypes.Integer64);
        claim.SetDestinations(Destinations.AccessToken);
        identity.AddClaim(claim);
    }

    // Build ClaimsIdentity for OpenIddict token generation (includes role claims)
    private async Task<ClaimsIdentity> CreateIdentity(ApplicationUser user)
    {
        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        identity.AddClaim(Claims.Subject, user.Id.ToString());
        identity.AddClaim(Claims.Email, user.Email ?? "");
        identity.AddClaim(Claims.Name, user.FullName);
        identity.AddClaim(Claims.PreferredUsername, user.UserName ?? user.Email ?? "");

        // Add role claims from ASP.NET Identity store
        var roles = await userManager.GetRolesAsync(user);
        foreach (var role in roles)
            identity.AddClaim(Claims.Role, role);

        // Permissions are NOT embedded in JWT — FE fetches via GET /api/v1/me/permissions
        // (React Query cached, staleTime 10min). Avoids 3-4KB token bloat for users with many perms.

        // Add tenant claim if user belongs to a tenant
        if (user.TenantId.HasValue)
            identity.AddClaim("tenant_id", user.TenantId.Value.ToString());

        // QĐ 742 1.6.b — emit mfa_enrolment_required claim when policy demands enrolment.
        // Single emission point covers all SignIn paths (auth-code, refresh, ROPC success), so
        // refresh-token grant rebuilds the claim from current DB state instead of caching.
        // Destinations set explicitly here so the SetDestinations switch below skips it.
        // Note: claim is serialized into the JWT as a JSON boolean (`true`), not the string `"true"`,
        // because of ClaimValueTypes.Boolean. FE must check truthiness, NOT === "true".
        // Runtime kill-switch + tenant override (QĐ742 1.6.b admin opt-out).
        // Tenant value wins over global; missing key → false. ROPC enrolment hard-block
        // (Branch B above) does NOT consult this — compliance hard-guarantee for CLI tooling.
        var enrolmentRequired = await mfaEnrolmentSettings.IsEnrolmentRequiredAsync(
            user.TenantId?.ToString(), HttpContext.RequestAborted);

        if (mfaOptions.Value.RedirectOnEnrolmentRequired)
        {
            var ct = HttpContext.RequestAborted;
            if (enrolmentRequired && await mfaEnrolmentEvaluator.RequiresEnrolmentAsync(user, ct))
            {
                var enrolClaim = new Claim(MfaEnrolmentRequiredClaimType, "true", ClaimValueTypes.Boolean);
                enrolClaim.SetDestinations(Destinations.AccessToken, Destinations.IdentityToken);
                identity.AddClaim(enrolClaim);

                // Audit log only on initial token issuance — refresh path runs every 15 minutes per
                // active session and would flood audit.AuditLogs + Hangfire HMAC chain otherwise.
                // Refresh = silent renew without user interaction; no new login event to record.
                var oidcRequest = HttpContext.GetOpenIddictServerRequest();
                var isRefresh = oidcRequest?.IsRefreshTokenGrantType() ?? false;
                if (!isRefresh)
                {
                    await auditService.LogAsync(
                        action: "Login.MfaEnrolmentRequired",
                        moduleName: "Identity",
                        resourceType: "User",
                        resourceId: user.Id.ToString(),
                        cancellationToken: ct);
                }
            }
        }

        // Set destinations: which claims go into access_token vs id_token
        foreach (var claim in identity.Claims)
        {
            // mfa_at is added OUTSIDE CreateIdentity (only on auth-code + refresh paths)
            // and has its destination set explicitly there — skip here to avoid override.
            // mfa_enrolment_required is set just above with explicit destinations — skip here too.
            if (claim.Type == MfaAtClaimType || claim.Type == MfaEnrolmentRequiredClaimType)
                continue;

            claim.SetDestinations(claim.Type switch
            {
                Claims.Name or Claims.PreferredUsername =>
                    new[] { Destinations.AccessToken, Destinations.IdentityToken },
                Claims.Email =>
                    new[] { Destinations.IdentityToken },
                // Roles must appear in both tokens — FE reads from id_token profile
                Claims.Role =>
                    new[] { Destinations.AccessToken, Destinations.IdentityToken },
                // Tenant ID needed by API for tenant-scoped operations
                "tenant_id" =>
                    new[] { Destinations.AccessToken, Destinations.IdentityToken },
                _ => new[] { Destinations.AccessToken }
            });
        }

        return identity;
    }
}
