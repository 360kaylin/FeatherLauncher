# User guide (Phase 2A)
**Account** honestly reports that Microsoft sign-in is unconfigured; there is no nonfunctional login button. Even with environment configuration, sign-in remains deliberately unavailable until Phase 2B.

**Versions** retrieves Mojang's official manifest. Releases are selected by default; select Snapshots, Old beta, or Old alpha to add them. Rows show ID, type, release date, and update date. **Storage** reports cache size, age, validity and offline availability, and provides refresh and clear actions. A valid cache is used for six hours; when refresh fails, a still-parseable cached manifest (including stale data) is shown with an offline label. Clearing it removes offline availability. No game files are downloaded.

Settings and Diagnostics retain Phase 1 behavior. To delete all local data, close the launcher and delete the application-data directory displayed in Diagnostics.
