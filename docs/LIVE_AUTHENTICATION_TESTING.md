# Controlled Windows live authentication testing

This procedure validates one local portable build; it does not prove the pipeline for every account or environment. Never upload or paste passwords, tokens, device codes, XUIDs, account responses, or unredacted logs.

## Register the public client

1. In Microsoft Entra admin center, create an **App registration**. Choose **personal Microsoft accounts only** unless the test plan explicitly also needs organizational accounts.
2. Copy the **Application (client) ID** (a GUID). It is an identifier, not a secret. Do not create or use a client secret.
3. Under **Authentication**, enable **Allow public client flows**. Device-code flow requires a public-client application. Use the `consumers` authority for personal Microsoft accounts.
4. In Feather Launcher open **Authentication Setup**, enter the client ID, `https://login.microsoftonline.com/consumers`, scopes `XboxLive.signin offline_access`, enable device-code flow and authentication, then save and restart. Confirm every validation line is positive and sign-in is enabled.

## Obtain and verify the portable build

Manually run **Windows live authentication test package** in GitHub Actions, or follow `BUILDING.md`. Download `FeatherLauncher-live-auth-test-win-x64`, compare the ZIP's SHA-256 with its `.sha256` file, extract it to a new folder, and run `FeatherLauncher.Desktop.exe`. The workflow needs no Microsoft credentials and performs no sign-in.

## Execute the checklist

Use **Authentication Setup** to mark Pass, Fail, or Not tested with a timestamp and short, non-sensitive note for each scenario:

1. Sign in with an account that owns Minecraft: Java Edition and confirm the ready state.
2. Sign out, then use an account without Java entitlement and confirm the safe `MinecraftNotOwned` result.
3. Start device-code sign-in and cancel it in Feather Launcher.
4. Start again, do not enter the code, and wait for expiry; confirm the code is cleared and expiry is categorized safely.
5. Sign in and test **Sign out**; restart and ensure the session is absent.
6. Test **Switch account**, then cancel one switch and complete another using a different test account.
7. For refresh coverage, leave an authenticated test session until expiry and verify silent refresh; then revoke consent/session and confirm reauthentication is requested without exposing a refresh token.
8. Interrupt the network during sign-in and refresh, restore it, and confirm a recoverable safe network category.
9. Inspect logs for categories only. Search locally for test email, account identifiers, codes and token fragments; none should occur. Do not share unredacted logs.
10. On **Diagnostics**, export the redacted report. Confirm it contains only environment/configuration status, safe categories, timestamps, and scenario labels—never client ID, local username/path, account data, codes, or tokens.

Saving all scenarios as Pass creates a configuration-bound local verification record. Changing enabled state, client ID, authority, scopes, or flow invalidates it.

## Cleanup

In **Authentication Setup**, check the confirmation and select **Clear authentication data**. This cancels sign-in, signs out, removes supported MSAL accounts, deletes the encrypted cache, clears authentication state/device-code state and authentication logs, but leaves unrelated settings and Minecraft data. In the Microsoft account privacy/security application-consent page, remove Feather Launcher's consent. Finally delete the Entra app registration if it is no longer needed. Delete exported reports if they are no longer required.
