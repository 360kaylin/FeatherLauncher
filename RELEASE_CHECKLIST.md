# Release checklist
- [ ] Confirm scope, version and changelog; freeze dependency versions
- [ ] Restore, format-check, Release build and all tests pass from clean checkout
- [ ] Review analyzers, dependency vulnerabilities/licenses and produce SBOM
- [ ] Verify no secrets, account data, logs, caches, game files or local settings are tracked
- [ ] Run Windows 10 and 11 x64 smoke/accessibility tests; record measured startup/memory
- [ ] Verify privacy/security/user documentation and unofficial branding
- [ ] Publish self-contained win-x64; malware scan; test clean-machine launch and settings reset
- [ ] Create portable ZIP; verify contents; calculate SHA-256
- [ ] Once implemented, verify installer upgrade/uninstall leaves user data only by explicit policy
- [ ] Sign final binaries/artifacts when signing infrastructure exists
- [ ] Tag immutable commit, upload checksums/SBOM, monitor and document rollback

## Phase 2B identity

- [ ] Confirm Release has no development/test provider or credential.
- [ ] Confirm sign-in stays disabled without a valid client ID and explicit feature flag.
- [ ] Exercise cancellation, denial, unowned account, sign-out, switching, and DPAPI cache deletion.
- [ ] Record live Microsoft/Xbox/XSTS/Minecraft testing separately; never infer it from mocked tests.
- [ ] Run the dependency vulnerability scan and validate the portable ZIP.

## Phase 2C controlled live authentication verification (still required)
Use a dedicated, correctly configured Microsoft Entra public-client registration. Record only pass/fail, timestamp, app version, and these scenario labels—never credentials, tokens, device codes, account identifiers, or raw responses.

- [ ] Owned Minecraft: Java Edition account reaches Ready and loads the expected profile.
- [ ] Microsoft account without Java entitlement receives the safe unowned-account message.
- [ ] Cancel an active device-code flow; polling stops and Signed out/failure is stable.
- [ ] Allow a device code to expire; expiry is shown and polling stops without sleeps in automated tests.
- [ ] Sign out from Ready; profile, entitlement, Minecraft token, MSAL accounts, and encrypted cache clear.
- [ ] Switch accounts; old profile/token disappear immediately and cancellation of the new flow is safe.
- [ ] After actual token expiry, silent refresh succeeds; revoked/failed refresh requires sign-in safely.
- [ ] Interrupt the network at Microsoft, Xbox, XSTS, Minecraft exchange, entitlement, and profile stages.
- [ ] Inspect UI, logs, exceptions, crash reports, and artifacts for secrets after every scenario.

The local record is an operator note, not universal proof. Phase 3 is gated on this checklist and review of remaining tenant policy, regional, family-account, service-outage, and MSAL platform behavior.
