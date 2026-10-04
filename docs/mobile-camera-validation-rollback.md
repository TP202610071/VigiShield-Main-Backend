# Mobile camera validation — deployment and rollback safeguards

Status: implementation in progress; this document is NOT a deployment-success record.

## Scope

- Add a phone/tablet video-only WebRTC source feeding the existing MediaMTX/RTSP detection pipeline. Do not change model weights or contextual-engine rules.
- Persist events even when a camera's user-facing notifications are disabled.
- Explicitly opt-in foreground validation session: screen evidence and sustained-risk test alarm. Test-call destination is allowlisted in the app; automatic real emergency dialing is not part of this release.
- Recording needs Android OS consent and an active buffer BEFORE an incident. A clip is centered on receipt in the app, not a claim of synchronization with the original camera incident. Early events may have less pre-roll. Physical-device verification is mandatory for capture, brightness, vibration and calling.

## Baseline and verified backups

Each repository has the baseline tag `backup/pre-mobile-camera-20261004` and work branch `feature/mobile-camera-validation`.

| Repository | Baseline commit |
| --- | --- |
| VigiShield_AI_Backend | `51a268d1342db1a75043e083debdbf3cd13808b7` |
| VigiShield_Main_Backend | `a4624da18d032e63f40395683e8dacd7e4c7b3b7` |
| VigiShield_MobileApp | `269e047bdb0f49f42fcd1b148c163efab7004ec6` |

Server snapshots are in `/var/backups/vigishield/pre-mobile-camera-20261004/`, owned by root with a private directory. A second copy is in the operator's private Hermes backup directory, outside all Git repositories. These archives contain private configuration: NEVER commit or distribute them with an APK.

| Artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| ai-runtime.tar.gz | 123819035 | `f7a70f6aedf2c6a022ac3231032312df04573daade24319721aebb05852110e9` |
| main-runtime.tar.gz | 52680161 | `9b4fa1fc2e82af37cc773689569b4827593916cc078c557ca3a62e491268e0e5` |
| database.dump | 399886 | `541a2ac03f4c32e78c2ce785ab6e029da283f0a44f14e7159c58b7e7705ff409` |

Verified: matching hashes of local copies; archive listings; PostgreSQL custom-dump TOC and full decode via `pg_restore --file=/dev/null`. This verifies archive readability, NOT a completed restore into a database. The server PostgreSQL 16 client could not dump the PostgreSQL 17 database; an isolated PostgreSQL 17 client and libpq were extracted under the operator's tools directory without replacing system packages.

## Deployment gate

Do not expose partially implemented publisher endpoints or run a migration merely because compilation passes. First require:

1. Backend authorization/tenant isolation, notifications persistence, WHIP origin/path validation and cleanup tests pass.
2. Flutter publisher lifecycle, mute behavior, alarm continuity/staleness/cancel/lifecycle tests pass.
3. Native ring-buffer/keyframe/path ownership tests and Android build pass.
4. Review the complete cross-component diff, including untracked files. Do not accidentally add model weights, credentials, recordings, private backups or generated build directories.
5. Verify current production health and make a FRESH database backup immediately before a deployment (the baseline snapshot is historical).
6. Review the additive migration SQL. Keep existing IP cameras and default notification behavior unchanged.
7. Configure a private WHIP gateway key on both servers, never in the mobile app or repository. Missing gateway configuration must leave publishing unavailable, not unauthenticated.
8. Keep direct WHIP signaling inaccessible publicly; do not disable existing viewer JWT checks. Advertise a reachable WebRTC media address and explicitly verify cloud/network media ingress.
9. Perform an authenticated disposable-camera signaling/media smoke test and verify an existing IP camera still works. Delete only smoke-test resources owned by the test account.
10. Deliver build and exact source commits. Clearly distinguish CI/unit checks from actual Android/iPhone hardware verification.

## Reversal order

1. Disable/stop validation sessions and publishing clients. Disable the new publisher configuration; confirm no new sessions are accepted. Retain evidence files unless their owner requests deletion.
2. Restore the previously deployed backend artifact and its matching runtime configuration from the private snapshot, after preserving a new snapshot of the current state. Extract into a staging directory and inspect members first; do not blindly untar into `/` or overwrite current secrets.
3. Restore only changed MediaMTX/Nginx settings from the baseline, validate with `nginx -t`, then restart/reload the affected services. Preserve unrelated configuration changes made since the snapshot.
4. Prefer leaving an unused additive notification column in place when running old code: this avoids deleting current event data. If a schema rollback is actually required, review the generated DOWN SQL, take a fresh database dump, and remove/reconcile mobile-source cameras before reverting the enum/application support. A full database restore is a disaster-recovery choice that loses data written since the snapshot and requires explicit approval.
5. Rebuild/distribute the previous mobile revision. Removing an app update on a phone requires installing a compatible signed build; Git rollback alone does not revert a user's installed application.
6. For source history, use reviewed `git revert` commits for the feature commits, not force pushes or destructive hard resets. The baseline tag is the comparison/rebuild point, not permission to discard unrelated later work.
7. Verify backend health, existing camera streams, AI worker connectivity, event persistence, and original notifications after reversal.

Record actual feature commits, migration identifier, deployment times, tests and smoke-test results in a separate delivery report once those actions have really completed.
