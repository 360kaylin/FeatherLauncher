# User guide (Phase 2A)
**Account** honestly reports that Microsoft sign-in is unconfigured; there is no nonfunctional login button. Even with environment configuration, sign-in remains deliberately unavailable until Phase 2B.

**Versions** retrieves Mojang's official manifest. Releases are selected by default; select Snapshots, Old beta, or Old alpha to add them. Rows show ID, type, release date, and update date. **Storage** reports cache size, age, validity and offline availability, and provides refresh and clear actions. A valid cache is used for six hours; when refresh fails, a still-parseable cached manifest (including stale data) is shown with an offline label. Clearing it removes offline availability. No game files are downloaded.

Settings and Diagnostics retain Phase 1 behavior. To delete all local data, close the launcher and delete the application-data directory displayed in Diagnostics.

## Accounts

If the repository owner configured Microsoft authentication, select **Sign in with Microsoft**. Open the displayed official verification URL in your normal browser and enter the temporary code. Never enter a Microsoft password in Feather. You can cancel while waiting, sign out, or switch account. Ready status requires the official service to confirm Minecraft: Java Edition ownership and return a valid profile; another Minecraft edition alone is not proof of Java ownership. If configuration is absent, the page states “Microsoft sign-in is not configured yet.”

## Controlled authentication testing
The Account page shows the Microsoft verification URL, temporary code, expiry, copy, and cancellation controls; enter no password in Feather Launcher. The Diagnostics page contains only configuration/flow/state/expiry/storage/error/manual-verification status. Live testing requires an operator-controlled Entra registration and the scenarios in `RELEASE_CHECKLIST.md`. Do not capture codes or tokens in screenshots or logs.
