# Microsoft public-client registration

Feather Launcher uses MSAL device-code authentication: the launcher shows Microsoft's verification URL and a temporary code, while the password is entered only into Microsoft in the user's normal browser. Device code avoids an embedded browser and a locally listening callback. Live testing is manual and opt-in; mocked CI does not prove live compatibility.

1. In the Microsoft Entra admin center, create an **App registration**. Select **Accounts in any organizational directory and personal Microsoft accounts** (personal Microsoft accounts are required for consumer Minecraft accounts).
2. Under **Authentication**, add the **Mobile and desktop applications** platform and enable **Allow public client flows**. Device code needs no redirect listener; use the documented desktop redirect `https://login.microsoftonline.com/common/oauth2/nativeclient` only if the portal requires one.
3. Copy the **Application (client) ID**. Never create or configure a client secret for Feather Launcher.
4. In the environment that starts Feather Launcher set:
   - `FEATHER_AUTH_ENABLED=true`
   - `FEATHER_MS_CLIENT_ID=<application-client-id>`
   - `FEATHER_MS_AUTHORITY=https://login.microsoftonline.com/consumers`
   - `FEATHER_MS_SCOPES="XboxLive.signin offline_access"`
   - `FEATHER_MS_USE_DEVICE_CODE=true`
5. Start the launcher, choose **Sign in with Microsoft**, visit the displayed Microsoft URL in a normal browser, and enter the temporary code. Feather never requests the password.
6. Test separately with an account that owns Minecraft: Java Edition and, if available, one that does not. The latter must remain signed-in identity only and not playable. Do not put credentials or tokens in tests, logs, screenshots, issues, Actions secrets, or artifacts.

Live tests are disabled by default and are not automated in pull requests (especially forks). Interactive testing requires only the environment variables above; no GitHub secret should contain a user token. To remove access, sign out in Feather, remove the application's consent from the Microsoft account, then delete the Entra app registration. Microsoft does not expose a general public-client endpoint that guarantees immediate revocation of every already-issued access token; account-cache removal and local DPAPI cache deletion are performed on sign-out.

## Phase 2C manual verification
Automated tests substitute only the injectable MSAL boundary; production uses MSAL.NET. Keep live tests explicitly enabled by a local operator and out of CI. With the public-client registration, run all scenarios in `RELEASE_CHECKLIST.md`, including owned/unowned accounts, cancellation, expiry, sign-out, switching, real refresh after expiry, and network interruption. Never paste client secrets (none are required for a public client), tokens, device codes, account identifiers, or raw responses into issues or artifacts. A material change to client ID, authority, redirect URI, scopes, or flow invalidates the local verification record.
