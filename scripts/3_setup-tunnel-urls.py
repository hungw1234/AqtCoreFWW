"""
AqtCoreFW — Auto-configure all files for Cloudflare Tunnel demo.

Updates 4 config targets:
  1. web/.env.local           — FE env vars (API + Auth URLs)
  2. AuthServer appsettings   — CORS origins + IssuerUri + SPA client redirect URIs
  3. API appsettings          — CORS origins + AuthorityUrl
  4. vite.config.ts           — allowedHosts for tunnel domain

Usage:
  python scripts/setup-tunnel-urls.py <FE_URL> <API_URL> <AUTH_URL>
  python scripts/setup-tunnel-urls.py --restore   (restore localhost config)
"""

import json
import re
import sys
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# File paths
FE_ENV = os.path.join(ROOT, "web", ".env.local")
AUTH_SETTINGS = os.path.join(ROOT, "src", "host", "AqtCoreFW.AuthServer", "appsettings.Development.json")
API_SETTINGS = os.path.join(ROOT, "src", "host", "AqtCoreFW.Api", "appsettings.Development.json")
VITE_CONFIG = os.path.join(ROOT, "web", "vite.config.ts")

# Localhost defaults
LOCAL_FE = "http://localhost:5173"
LOCAL_API = "http://localhost:5001"
LOCAL_AUTH = "http://localhost:5002"

def ok(msg):
    print(f"  [OK] {msg}")

def err(msg):
    print(f"  [X]  {msg}")


def update_fe_env(api_url, auth_url):
    """Update web/.env.local with tunnel or localhost URLs."""
    content = "\n".join([
        "VITE_APP_NAME=AqtCoreFW",
        "VITE_APP_SUBTITLE=He thong quan tri - Khung CNTT Chinh phu",
        "VITE_APP_LOGO_URL=",
        f"VITE_API_BASE_URL={api_url}",
        f"VITE_OIDC_AUTHORITY={auth_url}",
        "VITE_AUTH_CLIENT_ID=aqtcorefw-spa-dev",
    ])
    with open(FE_ENV, "w", encoding="utf-8") as f:
        f.write(content)
    ok(f"web/.env.local  (API={api_url}, Auth={auth_url})")


def update_json_file(path, updates, label):
    """Read JSON, apply updates dict, write back.

    A value of None DELETES the key instead of writing it, so --restore can return a file to
    its committed shape rather than to an equivalent-but-different one.
    """
    with open(path, "r", encoding="utf-8-sig") as f:
        data = json.load(f)

    for key_path, value in updates.items():
        keys = key_path.split(".")

        if value is None:
            # Walk without creating intermediates - deleting a key under a parent that does
            # not exist is a no-op, not a reason to materialize empty objects.
            obj = data
            for k in keys[:-1]:
                if not isinstance(obj, dict) or k not in obj:
                    obj = None
                    break
                obj = obj[k]
            if isinstance(obj, dict):
                obj.pop(keys[-1], None)
            continue

        obj = data
        for k in keys[:-1]:
            if k not in obj:
                obj[k] = {}
            obj = obj[k]
        obj[keys[-1]] = value

    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")
    ok(label)


def spa_client_uris(fe_url):
    """Build the OIDC SPA client's redirect + post-logout-redirect URI lists for fe_url.

    The /silent-renew.html entries are load-bearing: oidc-client-ts loads them in a hidden
    iframe when the refresh-token grant fails or expires, and an unwhitelisted URI yields
    400 invalid_request (OAuth 2.0 4.1.2.1) even though the user-visible signinRedirect
    path only ever uses /callback.

    Configured values REPLACE the seeder's Development fallback rather than merging with it,
    so every origin the local tooling needs must be listed here too - notably localhost:3000,
    which infra/docker/smoke-test.sh hardcodes.
    """
    origins = list(dict.fromkeys([
        fe_url,
        LOCAL_FE,
        "http://localhost:3000",
        "http://localhost:4200",
        "http://127.0.0.1:5173",
    ]))

    redirect = []
    for origin in origins:
        redirect.append(f"{origin}/callback")
        redirect.append(f"{origin}/silent-renew.html")
    # Swagger's OAuth2 redirect always points at the local API host over HTTPS - Swagger UI is
    # served on the HTTPS profile, and the URI must match the seeder's Development fallback
    # exactly or the swagger "Authorize" round-trip breaks while a tunnel is configured.
    redirect.append("https://localhost:5001/swagger/oauth2-redirect.html")

    # ORDER IS LOAD-BEARING: slashless FIRST. new Uri("http://h:p") equals new Uri("http://h:p/"),
    # so the seeder's ISet<Uri> keeps only the first spelling, OpenIddict stores its
    # OriginalString, and logout matching is an ordinal string compare. oidc-client-ts sends
    # window.location.origin (always slashless), so a slash-terminated stored value 400s.
    post_logout = []
    for origin in origins:
        post_logout.append(origin)
        post_logout.append(f"{origin}/")

    return redirect, post_logout


def update_auth_settings(fe_url, api_url, auth_url):
    """Update AuthServer CORS + IssuerUri, and add/remove the SPA client redirect URIs."""
    cors_origins = [LOCAL_FE, "http://localhost:3000", LOCAL_API, fe_url, api_url]
    # Remove duplicates (when restoring localhost, fe_url == LOCAL_FE)
    cors_origins = list(dict.fromkeys(cors_origins))

    updates = {
        "Auth.IssuerUri": auth_url,
        "Cors.AllowedOrigins": cors_origins,
    }

    if fe_url.startswith("http://localhost"):
        # Restore: DELETE the section rather than rewriting it with the fallback's values.
        # The committed appsettings.Development.json deliberately carries no Auth:SpaClient -
        # the AuthServer's own Development fallback is the single source of truth for the
        # zero-config dev loop, and the Startup smoke test asserts the fallback branch. Writing
        # an equivalent-but-present section here would silently re-point that test at the
        # configured branch while its assertions still read as fallback coverage.
        updates["Auth.SpaClient"] = None
        label = f"AuthServer  IssuerUri={auth_url}, SPA client section removed"
    else:
        redirect_uris, post_logout_uris = spa_client_uris(fe_url)
        updates["Auth.SpaClient.RedirectUris"] = redirect_uris
        updates["Auth.SpaClient.PostLogoutRedirectUris"] = post_logout_uris
        label = f"AuthServer  IssuerUri={auth_url}, SPA redirect={fe_url}/callback"

    update_json_file(AUTH_SETTINGS, updates, label)


def update_api_settings(fe_url, api_url, auth_url):
    """Update API CORS + AuthorityUrl."""
    cors_origins = [LOCAL_FE, "http://localhost:3000", fe_url, api_url]
    cors_origins = list(dict.fromkeys(cors_origins))

    update_json_file(API_SETTINGS, {
        "Auth.AuthorityUrl": auth_url,
        "Cors.AllowedOrigins": cors_origins,
    }, f"API         AuthorityUrl={auth_url}")


def update_vite_config(fe_url):
    """Ensure vite allowedHosts includes tunnel domain."""
    with open(VITE_CONFIG, "r", encoding="utf-8") as f:
        content = f.read()

    if fe_url.startswith("http://localhost"):
        # Restore: remove allowedHosts line
        content = re.sub(r"\s*allowedHosts:.*,?\n", "\n", content)
        ok("vite.config.ts  (allowedHosts removed)")
    else:
        # Extract domain from URL
        domain = fe_url.replace("https://", "").replace("http://", "").split("/")[0]
        suffix = "." + ".".join(domain.split(".")[-2:])  # e.g. .trycloudflare.com

        if "allowedHosts" in content:
            content = re.sub(
                r"allowedHosts:.*,",
                f"allowedHosts: ['{suffix}'],",
                content
            )
        else:
            content = content.replace(
                "port: 5173,",
                f"port: 5173,\n    allowedHosts: ['{suffix}'],"
            )
        ok(f"vite.config.ts  allowedHosts=['{suffix}']")

    with open(VITE_CONFIG, "w", encoding="utf-8") as f:
        f.write(content)


def main():
    print()
    print("  =============================================")

    if len(sys.argv) == 2 and sys.argv[1] == "--restore":
        print("  Restoring localhost config...")
        print("  =============================================")
        print()
        update_fe_env(LOCAL_API, LOCAL_AUTH)
        update_auth_settings(LOCAL_FE, LOCAL_API, LOCAL_AUTH)
        update_api_settings(LOCAL_FE, LOCAL_API, LOCAL_AUTH)
        update_vite_config(LOCAL_FE)
        print()
        print("  Da restore ve localhost!")
        print("  Restart services: scripts\\run-local-dev.bat")
        print()
        return

    if len(sys.argv) == 4:
        fe_url = sys.argv[1].rstrip("/")
        api_url = sys.argv[2].rstrip("/")
        auth_url = sys.argv[3].rstrip("/")
    else:
        print("  Cap nhat Tunnel URLs")
        print("  =============================================")
        print()
        fe_url = input("  Frontend URL: ").strip().rstrip("/")
        api_url = input("  API URL:      ").strip().rstrip("/")
        auth_url = input("  Auth URL:     ").strip().rstrip("/")

    if not fe_url or not api_url or not auth_url:
        err("Thieu URL! Can nhap day du 3 URLs.")
        sys.exit(1)

    print()
    print(f"  FE:   {fe_url}")
    print(f"  API:  {api_url}")
    print(f"  Auth: {auth_url}")
    print()
    print("  Dang cap nhat 5 config files...")
    print()

    update_fe_env(api_url, auth_url)
    update_auth_settings(fe_url, api_url, auth_url)
    update_api_settings(fe_url, api_url, auth_url)
    update_vite_config(fe_url)

    print()
    print("  =============================================")
    print("  Da cap nhat xong! Buoc tiep:")
    print("  =============================================")
    print()
    print("  1. Restart services: scripts\\run-local-dev.bat")
    print(f"  2. GUI demo:        {fe_url}")
    print()
    print("  Khi xong demo, restore localhost:")
    print("    python scripts\\setup-tunnel-urls.py --restore")
    print()


if __name__ == "__main__":
    main()
