<div align="center">
  <img src="src/TravelMemory.Web/public/icons/travel-memory-192.png" alt="" width="96" />
  <h1>Travel Memory</h1>
  <p>A private digital travel memory, not a trip planner or photo manager.</p>
</div>

Travel Memory preserves trips as personal memories. See the
[product and technical roadmap](ROADMAP.md) for completed milestones, recommended next
steps, and explicitly exploratory ideas.

## Implemented vertical slices

The trip slice provides one complete core workflow:

1. Create a trip with a title and optional dates.
2. View the trip in the archive.
3. Open the trip detail page.
4. Persist data in SQL Server across application restarts.

Every trip is stored with a server-assigned `OwnerId`. Local development uses a fixed
development identity, but the API boundary already requires authentication and scopes
every query to the current owner.

The photo import slice adds:

- direct browser upload of up to 500 JPEG or HEIC files to private temporary Blob Storage
- 15-minute, per-blob SAS tokens with Create/Write permissions only, plus token renewal
- four concurrent uploads, stable batch/file idempotency, polling, and resume after file
  re-selection
- EXIF capture time, preservation of any EXIF UTC offset, and one separate time-adjustment
  delta per batch
- a preview of corrected local timeline times before finalization
- owner/trip-scoped SHA-256 duplicate detection
- a separate Queue worker with explicit job state, retries, and abandoned-job recovery
- orientation-normalized web JPEGs and thumbnails, exposed through a chronological photo
  timeline

## Technology

- React 19, TypeScript, and Vite
- installable PWA with app-shell caching but no offline writes
- ASP.NET Core and EF Core on .NET 10
- SQL Server 2025
- .NET Aspire with ServiceDefaults, OpenTelemetry, and health checks
- Azurite for local Blob and Queue emulation
- Magick.NET 14.16 for JPEG/HEIC decoding, EXIF, and orientation
- Oxlint with type-aware TypeScript rules

## Prerequisites

- .NET SDK 10.0.204 or a newer .NET 10 patch
- Aspire CLI 13.5.3
- Node.js 24.19.0
- Docker Desktop or a compatible Docker engine

## Run locally

From the repository root:

```powershell
dotnet tool restore
npm ci --prefix "src\TravelMemory.Web"
aspire start --non-interactive
```

Open the `web` endpoint from the Aspire dashboard. The AppHost starts:

- `sql` and the `travelmemory` database
- `storage` with the Azurite `blobs` and `queues` services
- `api`
- `worker`
- `web`

The API applies outstanding checked-in EF Core migrations automatically during
Development startup.

> [!NOTE]
> `Identity:DevelopmentOwnerId` in `appsettings.Development.json` is a stable local
> identity, not a secret. The development authentication handler cannot run outside the
> Development environment. A real authentication provider must be added before production
> use.

## Test and quality checks

```powershell
dotnet test "tests\TravelMemory.Api.Tests\TravelMemory.Api.Tests.csproj"
npm --prefix "src\TravelMemory.Web" run lint
npm --prefix "src\TravelMemory.Web" run typecheck
npm --prefix "src\TravelMemory.Web" test
npm --prefix "src\TravelMemory.Web" run build
```

The API tests use an ephemeral SQL Server 2025 container to verify migrations,
create/list/open, persistence, and isolation between two owners. The photo import tests
also use Azurite and verify JPEG/HEIC handling, orientation, the 500-file limit, SAS
permissions, time adjustment, duplicates, retries, derivatives, original deletion, and
visible retention after a processing failure.

## API

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/trips/` | Create a trip |
| `GET` | `/api/trips/` | List trips owned by the current user |
| `GET` | `/api/trips/{id}` | Open an owner-scoped trip |
| `POST` | `/api/trips/{id}/photo-imports` | Create or idempotently resume an import batch |
| `GET` | `/api/photo-imports/{id}` | Poll batch and per-file status |
| `POST` | `/api/photo-imports/{id}/items/{itemId}/upload-token` | Renew a per-file upload SAS |
| `POST` | `/api/photo-imports/{id}/items/{itemId}/complete-upload` | Verify an upload and queue analysis |
| `GET` | `/api/photo-imports/{id}/time-preview` | Preview a batch time adjustment |
| `POST` | `/api/photo-imports/{id}/finalize` | Lock the time adjustment and queue processing |
| `POST` | `/api/photo-imports/{id}/items/{itemId}/retry` | Retry an actionable file failure |
| `GET` | `/api/trips/{id}/photos` | Get the chronological timeline with short-lived read SAS URLs |

Trip dates are timezone-free calendar dates (`YYYY-MM-DD`). `CreatedAtUtc` is a UTC
instant. If both dates are present, the end date cannot precede the start date.

`CapturedAtOriginalLocal` is the camera's local EXIF wall-clock time with no assumed
timezone. `TimeAdjustmentMinutes` is the user's signed delta for the entire batch.
`CapturedAtTimelineLocal` is the corrected local wall-clock time. Any
`ExifOffsetMinutes` value is preserved separately. The original file and its metadata are
never modified.

## Processing and safe cleanup

The database job is authoritative; a Queue message is only an idempotent wake-up signal.
Worker stages are `Analyze`, `Process`, `Cleanup`, and, after a failure,
`ExpireFailedOriginal`.

1. SHA-256 and EXIF metadata are read from the temporary original.
2. Derivatives are written to deterministic private blob names and verified.
3. Photo metadata and both derivative blob names are persisted atomically in SQL Server.
4. Only then does cleanup delete the temporary original.

If processing fails, the temporary original is retained for seven days with a visible
retention deadline, preserving a diagnostic and reprocessing window without silent data
loss. Retryable failures can be retried through the API. Incomplete uploads expire after
24 hours. If original cleanup fails after an otherwise successful import, the item enters
`CleanupFailed` and cleanup can be retried; persisted derivatives and metadata are not
deleted.

## HEIC runtime

The HEIC choice was verified through a technical spike in
`mcr.microsoft.com/dotnet/runtime:10.0` on Ubuntu 24.04:

- `Magick.NET-Q8-AnyCPU` 14.16 decodes HEIC and reads `DateTimeOriginal`.
- `AutoOrient()` normalizes EXIF orientation before resizing.
- The NuGet package bundles the native ImageMagick/libheif decode runtime. No additional
  `apt` packages are required in a glibc-based Ubuntu container.
- Both derivatives are JPEG files. HEIC encoding is not required.

> [!IMPORTANT]
> Use a glibc-based runtime such as the verified Ubuntu image. Do not select Alpine/musl
> without a new native runtime spike.

## Repository structure

```text
src/
  TravelMemory.AppHost/         Aspire resource graph
  TravelMemory.Domain/          Trips, photo imports, jobs, and time semantics
  TravelMemory.Persistence/     EF Core mappings and migrations
  TravelMemory.ServiceDefaults/ Health, discovery, and telemetry
  TravelMemory.Api/             Authentication, SAS, and Minimal API contracts
  TravelMemory.Worker/          Queue, EXIF, derivative, and cleanup pipeline
  TravelMemory.Web/             React/Vite PWA
tests/
  TravelMemory.Api.Tests/       Domain and SQL/Azurite-backed integration tests
```

## Local data

SQL Server and Azurite use named Docker volumes. Stop the AppHost before resetting local
data:

```powershell
aspire stop --non-interactive
docker volume ls
docker volume rm <exact-volume-name-from-Aspire>
```

> [!CAUTION]
> Use the exact volume name shown in the Aspire resource details. Do not use
> `docker volume prune`, because it can delete data belonging to other local projects.

## Explicitly out of scope

GPX/FIT and GPS matching, SignalR, AI captioning/embeddings, weather, real authentication
and family sharing, trip/photo editing and deletion, offline trip data, and Azure
deployment are not part of the implemented slices.

For the recommended order of future work, see [ROADMAP.md](ROADMAP.md).
