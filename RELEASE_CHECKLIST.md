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
