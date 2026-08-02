# Security policy
Report vulnerabilities privately through GitHub Security Advisories. Never submit live tokens, passwords, IDs, or personal game files. Cracked/offline bypasses, password collection, session theft, weakened TLS/hash validation, and fake entitlement are out of scope and will not be accepted.

Phase 2A accepts only HTTPS metadata URLs, bounds documents, validates required JSON fields/types/IDs/dates and supplied SHA-1 hashes for individual metadata, and atomically saves a validated manifest. Its cache is not an authorization source. Authentication is deliberately disabled: the contracts and DPAPI vault are preparation, not a claim of working login. See [docs/IDENTITY_THREAT_MODEL.md](docs/IDENTITY_THREAT_MODEL.md). Local malware running as the same user remains outside the launcher's protection boundary.

## Authentication boundary

Authentication is fail-closed when public-client configuration or Windows DPAPI storage is unavailable. Every service stage requires HTTPS, bounded JSON, cancellation, and a timeout; remote bodies and credentials are never logged or surfaced in UI exceptions. Report any token disclosure privately. Sign-out removes the local account/cache but cannot promise immediate remote revocation of every issued public-client token.
