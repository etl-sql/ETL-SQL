# Object-Native Artifact Storage Contract

**Status:** Accepted and implemented (Platform Phase 1)
**Decision date:** 2026-08-24

## Context

`IArtifactStorage` was designed for local and SMB filesystems. Its atomic-write guarantee is
implemented with a same-filesystem rename. S3-compatible stores and Azure Blob Storage expose
objects, opaque ETags/version IDs, and conditional requests; they do not expose a portable atomic
rename. Treating copy followed by delete as rename can publish incomplete or stale state after a
crash, timeout, retry, or lease loss.

This decision adds an object-native protocol on top of the existing artifact-area/path model. The
filesystem providers remain valid for Solo and SMB deployments. Object-backed shared content uses
`IObjectStore` plus `ObjectNativeArtifactStorage`; callers never infer filesystem behavior from an
object provider.

## Decision

An artifact has three object forms:

- **Staging object** — `etlsql/v1/staging/{operation-id}`. It is unique, non-authoritative, and may
  be deleted after publication or by retention-based garbage collection.
- **Immutable content object** — `etlsql/v1/objects/sha256/{prefix}/{sha256}`. Its key is the hash of
  the complete content. Creation is conditional (`If-None-Match: *`). An existing object is accepted
  only when its stored hash metadata agrees.
- **Commit record** — `etlsql/v1/commits/{area}/{logical-path-hash}`. This JSON record contains the
  logical area/path, content hash/key/length/type, fence token, operation ID, and commit time. It is
  the sole publication authority. Readers never discover content by listing staging or content keys.

Publication is ordered as follows:

1. Stream the caller's bytes to a unique staging key while computing SHA-256 and length.
2. Conditionally materialize the immutable content key. A completed copy is still invisible.
3. Acquire the logical artifact's database-backed mutation lock and claim its monotonic epoch
   through `IWriteEpochStore`. A lower token fails closed. The lock spans the epoch check and commit
   request, closing the cross-system window in which a newer writer could otherwise fence an older
   writer while the older object request was in flight.
4. Read the current commit and conditionally create or replace it using its opaque ETag/version ID.
   A competing update causes a re-read and bounded retry. A commit with a newer fence rejects the
   writer.
5. Best-effort delete staging. Failure is safe because staging is not authoritative.

The protocol deliberately does **not** emulate POSIX atomic rename. In particular, an object copy
followed by source deletion is never described or exposed as an atomic move. Copy may be used only
to produce an immutable, non-authoritative content object; visibility changes in the separate
conditional commit operation.

## Provider contract

`IObjectStore` has only object operations: get, prefix-list, conditional put, conditional copy, and
conditional delete. Versions are opaque. Providers must map failed `If-Match`/`If-None-Match`
conditions to `ObjectStorePreconditionFailedException`; they must not implement check-then-write in
process. `AzureBlobObjectStore` maps the contract to Blob request conditions.
`S3ObjectStore` maps it to S3 conditional requests. Both stream copy through a GET and conditional
PUT because destination-copy preconditions vary among S3-compatible services; this is correct
because the destination is immutable and remains non-authoritative until commit.

The existing Apache-2.0 `AWSSDK.S3` and MIT `Azure.Storage.Blobs` dependencies supply the two
providers. They were already version-pinned and inventoried; the AWS notice is included with the
other runtime notices.

## Failure model

| Failure | Required outcome | Recovery/evidence |
| :--- | :--- | :--- |
| Upload stops partway | No commit exists; readers see the prior commit or absence | Provider abort/error; abandoned staging GC |
| Immutable copy fails | No visibility change | Retry publication; CAS object may be safely reused |
| Writer loses database fence | Commit is refused | `FencedWriteException`; bytes remain non-authoritative |
| Concurrent commit wins | Conditional write fails | Re-read opaque version, reject newer fence or retry |
| Commit response is lost | Outcome is unknown, not assumed failed | Re-read commit; matching operation ID and hash proves success |
| Retry repeats upload | No duplicate authoritative artifact | Unique staging plus content-addressed deduplication |
| Provider outage | Prior commit remains authoritative | Surface failure; retry after recovery |
| Cleanup outage | Staging residue remains invisible | Reconciler deletes entries older than retention |
| Commit references missing/corrupt content | Never silently treat it as healthy | Read fails closed; reconciliation reports missing/hash mismatch |

Commit records are not garbage-collected from a mere object listing. Immutable content collection
requires a future mark/sweep policy over authoritative commits and retention evidence. This phase
only collects abandoned staging, which can never be live.

## Reconciliation and certification

`ReconcileAsync` scans authoritative commits, streams and verifies every referenced immutable
object's length, SHA-256 content, and hash metadata, reports missing/corrupt references, and
conditionally deletes expired staging by opaque version. It does not repair by guessing. Portal's
`ObjectArtifactMaintenanceService` runs this pass at startup and on the configured interval.

The provider-neutral hostile suite certifies concurrent writers, stale fences, partial staging,
lost commit responses, conditional retries, outages, reconciliation, staging collection, and an
8 MiB streaming portability payload. The shared integration contract runs unchanged against MinIO
(S3 API) and Azurite (Azure Blob), proving both providers' conditional-version and object-native
publication behavior.

## Consequences

- Shared mutations cannot expose partial bytes: only a complete immutable object can be committed.
- Stale nodes cannot publish after a newer database fence or newer commit record.
- Object storage configuration must supply both the provider and the same shared relational fencing
  authority used by the cluster; node-local epochs are invalid in production.
- Existing consumers that require a local filename or atomic rename stay on `IArtifactStorage` until
  explicitly converted. `ObjectNativeArtifactStorageAdapter` integrates direct-write/read/list/delete
  consumers and local-copy leases, while explicitly rejecting `MoveAsync`. Portal routes Scripts,
  Snapshots, Datasets, and Maps through it for S3/Azure configurations and keeps Keys on the shared
  filesystem key ring.

## References

- [Engine architecture — Artifact Storage](../Engine.md#artifact-storage)
- [State and high availability](../../administration/platform/state-and-ha.md)
- [Tenant portability architecture](../tenant-portability.md)
