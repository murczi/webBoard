# Audit implementation

All 35 planned tasks from the [baseline audit](CODEBASE_AUDIT.md) are addressed individually on `fix/codebase-audit`. Each task has its own commit and passed a Release solution build, all .NET tests against disposable PostgreSQL, and the JavaScript tests before commit. Form and styling changes also passed real Chromium browser checks; deployment changes passed installer tests. Builds now treat warnings as errors. A final running-container check uncovered one additional packaging defect, addressed separately as F15: the UI runtime image lacked PostgreSQL's native GSSAPI authentication dependency. CI now runs database migrations from the built UI image to catch this regression.

| Task | Result | Commit |
| --- | --- | --- |
| F01 | Enforce current account and resource grants for sessions and mutations | [f6df04b](https://github.com/murczi/webBoard/commit/f6df04b) |
| F02 | Restore trusted proxy scheme and enforce secure production cookies | [a01daab](https://github.com/murczi/webBoard/commit/a01daab) |
| F03 | Discard stale permission and host health editor responses | [47cb38d](https://github.com/murczi/webBoard/commit/47cb38d) |
| F04 | Preserve populated legacy schema during pending historical migrations | [36e9178](https://github.com/murczi/webBoard/commit/36e9178) |
| F05 | Archive Steam configuration and preserve modules during downgrade | [54b7370](https://github.com/murczi/webBoard/commit/54b7370) |
| F06 | Allow offline host disabling and metadata edits | [731f1fe](https://github.com/murczi/webBoard/commit/731f1fe) |
| F07 | Require Minecraft ports and share game server address validation | [79d9ac3](https://github.com/murczi/webBoard/commit/79d9ac3) |
| F08 | Unify systemd unit validation across domain agent and installer | [8549158](https://github.com/murczi/webBoard/commit/8549158) |
| F09 | Validate persisted URL length after normalization | [321763c](https://github.com/murczi/webBoard/commit/321763c) |
| F10 | Enforce database canonical host uniqueness and translate conflicts | [b42280b](https://github.com/murczi/webBoard/commit/b42280b) |
| F11 | Preserve typed operation uncertainty timeout and partial completion | [cb5fb7f](https://github.com/murczi/webBoard/commit/cb5fb7f) |
| F12 | Share effective module host eligibility and paused-state feedback | [87c2b84](https://github.com/murczi/webBoard/commit/87c2b84) |
| F13 | Distinguish expired retention from gaps in history slices | [4e1f8fc](https://github.com/murczi/webBoard/commit/4e1f8fc) |
| F14 | Bound agent payload parsing and return structured discovery failures | [e9a7c66](https://github.com/murczi/webBoard/commit/e9a7c66) |
| P01 | Load operation capabilities on demand instead of during page rendering | [4394f0b](https://github.com/murczi/webBoard/commit/4394f0b) |
| P03 | Initialize the dummy password hash once while preserving verification | [4efae8f](https://github.com/murczi/webBoard/commit/4efae8f) |
| P02 | Maintain indexed monitoring blocks for bounded history inspection | [7b9de3e](https://github.com/murczi/webBoard/commit/7b9de3e) |
| S01 | Encapsulate validated module configurations behind type-specific policies | [ca74f2b](https://github.com/murczi/webBoard/commit/ca74f2b) |
| S02 | Centralize permission resources and effective action implications | [82bd951](https://github.com/murczi/webBoard/commit/82bd951) |
| S03 | Centralize validated agent endpoint construction and profile matching | [21a8296](https://github.com/murczi/webBoard/commit/21a8296) |
| S04 | Use injected UTC clocks and consistent timestamp presentation | [4c60e87](https://github.com/murczi/webBoard/commit/4c60e87) |
| S05 | Share module projections and serialize one editor payload | [88adfe4](https://github.com/murczi/webBoard/commit/88adfe4) |
| S06 | Separate operation orchestration and atomic permission persistence | [e493d39](https://github.com/murczi/webBoard/commit/e493d39) |
| S07 | Share bounded process capture for discovery and injected systemd controls | [c890497](https://github.com/murczi/webBoard/commit/c890497) |
| S08 | Show every effective permission using shared resource presentation metadata | [aa25978](https://github.com/murczi/webBoard/commit/aa25978) |
| S09 | Run frontend installer and real HTTPS browser checks in CI | [26fc224](https://github.com/murczi/webBoard/commit/26fc224) |
| S10 | Standardize resource errors validation feedback and safe diagnostic context | [eeda63c](https://github.com/murczi/webBoard/commit/eeda63c) |
| S11 | Reject stale host module and permission edits atomically | [13a2760](https://github.com/murczi/webBoard/commit/13a2760) |
| S12 | Preserve monitoring freshness for metadata edits with dedicated configuration versions | [253ee9d](https://github.com/murczi/webBoard/commit/253ee9d) |
| S13 | Use consistent environment variable precedence for application and migrations | [14af0bd](https://github.com/murczi/webBoard/commit/14af0bd) |
| S14 | Remove obsolete styles and provide clear themed error guidance | [7734ebf](https://github.com/murczi/webBoard/commit/7734ebf) |
| S15 | Generate base-aware browser endpoints and support prefixed deployments | [68b43c7](https://github.com/murczi/webBoard/commit/68b43c7) |
| S16 | Centralize supported module types and reject unsupported configuration writes | [f3df8fb](https://github.com/murczi/webBoard/commit/f3df8fb) |
| S17 | Own agent payloads in a shared versioned wire contract | [e45f12b](https://github.com/murczi/webBoard/commit/e45f12b) |
| S18 | Standardize namespaces, UTF-8 token rules, stable enums, and project conventions | [6da4123](https://github.com/murczi/webBoard/commit/6da4123) |
| F15 | Include PostgreSQL native GSSAPI dependency and verify migrations from the built UI image in CI | This commit |

## Object-oriented structure

The project was already object-oriented through dependency injection, service/repository interfaces, and health handler implementations. The changes strengthen that design with immutable validated configuration objects, shared value objects and catalogs, authorization decorators, and a domain operation coordinator backed by infrastructure adapters. Business rules have explicit owners, and UI/agent payloads have a shared contract assembly. Composition is used for responsibilities; the small operation result adapters retain existing source names while sharing wire fields and defaults.

SQL queries, projection expressions, browser event handling, and stateless validation functions do not need inheritance. Further OO work should follow concrete behavior or extension requirements, rather than converting every data record into a class hierarchy. Classes with no extension contract are sealed. Service dependencies use interfaces and the same injectable clock pattern. Shared project properties and `.editorconfig` define build and formatting conventions; namespaces use `Webboard`. The agent project directory and executable retain `WebBoard.Agent` for deployment compatibility.

## Verification and operational changes

- PostgreSQL tests cover populated historical upgrades and rollback preservation, stale writes, revocation, operation replay/locking, monitoring projections, metadata freshness, and configuration invalidation.
- Browser checks cover registration and secure cookies behind an HTTPS proxy, module create/edit, concurrent forms, permission request races, immediate revocation/deletion, history data, error copy, and light/dark themes. The same suite supports root deployment and `/board` prefix deployment.
- Agent tests exercise bounded process execution, timeouts, Docker name rediscovery using a mock Unix socket, systemd command construction with a fake runner, request authentication/allowlists, and legacy/current protocol compatibility. They do not execute privileged systemd operations on the host.
- Installer tests mock privileged/lifecycle commands and validate generated settings. The final runtime checks below also exercise both built images.

New forward migrations normalize host uniqueness, preserve operation outcomes, materialize history blocks, standardize UTC timestamps, add stale-edit versions, and separate monitoring freshness from metadata edit versions. The host normalization migration stops with a clear error if historical case-insensitive duplicate names exist; it does not delete or merge hosts. Steam rollback preserves affected configuration in `ModuleRollbackArchive` for manual restoration. The legacy upgrade fixes retain their migration IDs and only affect migrations still pending in an installation.

Existing monitoring results remain available; snapshots predating dedicated monitoring versions are checked again by the collector. Session access now reflects current database grants, so refresh is no longer required to enforce revocation. Unknown module types remain visibly unsupported on reads and are rejected on writes. Agent version 1 preserves the original JSON format and accepts legacy missing version headers.

A history query benchmark on a disposable database with 86,400 identical samples reduced the measured block lookup from roughly 199 ms to 0.034 ms after coalescing to one projected block. This verifies the intended query change for that fixture, not production latency under arbitrary loads.


## Final verification — 12 October 2026

| Check | Result |
| --- | --- |
| Release solution build | Passed; 0 warnings, 0 errors |
| .NET tests with disposable PostgreSQL | 215 passed, 0 failed, 0 skipped |
| JavaScript tests | 7 passed |
| Installer tests | 58 passed |
| Chromium browser suite at root | 3 passed |
| Chromium browser suite under `/board` | 3 passed |
| EF model/migration consistency | No pending model changes |
| UI Docker image | Built successfully; fresh database migration, registration, secure session cookie, and static asset checks passed |
| Agent Docker image | Built successfully; authentication, protocol version 1 health, unsupported-version rejection, and denied control checks passed |
| Whitespace/conflict-marker checks | Passed |

The checks use disposable databases and containers; no production database migrations or privileged host service actions were performed. The branch contains 36 commits: one for each of the 35 planned tasks and one for F15 discovered during final container verification. The push publishes the feature branch for review; deployment and merging are separate actions.
