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

Every trip is stored with a server-assigned `OwnerId`. Users sign in through OpenID
Connect, each provider identity maps to one internal owner, and every query is scoped to
the current owner. See [Authentication](#authentication).

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
- Keycloak as the local OpenID Connect provider
- Magick.NET 14.16 for JPEG/HEIC decoding, EXIF, and orientation
- Oxlint with type-aware TypeScript rules

## Prerequisites

- .NET SDK 10.0.204 or a newer .NET 10 patch
- Aspire CLI 13.5.3
- Node.js 24.19.0
- Docker Desktop or a compatible Docker engine with the buildx plugin. Aspire builds a
  small tunnel image with it so that the Keycloak container can reach the dashboard on
  the host. On Ubuntu's `docker.io` package, install it with `sudo apt install docker-buildx`.

## Run locally

From the repository root:

```powershell
dotnet tool restore
npm ci --prefix "src\TravelMemory.Web"
aspire start --non-interactive
```

Open the app at <http://localhost:5173>. The AppHost starts:

- `sql` and the `travelmemory` database
- `storage` with the Azurite `blobs` and `queues` services
- `keycloak` on <http://localhost:8180> with the `travel-memory` realm
- `api`
- `worker`
- `web`

The API applies outstanding checked-in EF Core migrations automatically during
Development startup.

The app redirects to Keycloak to sign in. The realm in
`src/TravelMemory.AppHost/Realms/travel-memory-realm.json` defines two local test users,
`alice` and `bob`, with their passwords. Signing in as both is the quickest way to see
that each user only sees their own trips.

> [!NOTE]
> Keycloak and the web app use fixed ports, because the issuer URL is part of every
> user's identity mapping and the realm only allows the web app's callback URL. The OIDC
> client secret is an Aspire parameter that is generated on first run and stored in the
> AppHost's user secrets. The realm reads it from an environment variable, so no secret is
> committed.

## Test and quality checks

```powershell
dotnet test --project "tests\TravelMemory.Domain.Tests"
dotnet test --project "tests\TravelMemory.IntegrationTests"
dotnet test --project "tests\TravelMemory.EndToEndTests"
npm --prefix "src\TravelMemory.Web" run lint
npm --prefix "src\TravelMemory.Web" run typecheck
npm --prefix "src\TravelMemory.Web" test
npm --prefix "src\TravelMemory.Web" run build
```

The domain tests run without Docker. The integration tests use an ephemeral SQL Server
2025 container to verify migrations, create/list/open, persistence, sign-in mapping, and
isolation between two owners. The photo import tests
also use Azurite and verify JPEG/HEIC handling, orientation, the 500-file limit, SAS
permissions, time adjustment, duplicates, retries, derivatives, original deletion, and
visible retention after a processing failure.

The end-to-end test starts the real Aspire resource graph with `Aspire.Hosting.Testing`.
It signs in as `alice` through Keycloak's login form, creates a trip, and imports one
photo through the API, Blob Storage, and the worker. It needs Docker, the web app's npm
packages (`npm ci`), and free ports 5173 and 8180, so stop a running AppHost first.

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

## Authentication

The API acts as a backend for the SPA (the BFF pattern). It runs the OpenID Connect
authorization code flow with PKCE itself and gives the browser only an `HttpOnly`,
`Secure`, `SameSite=Lax` session cookie. No token is ever readable by JavaScript. This
needs no extra server, because the API already serves the SPA from the same origin. In
development, the Vite proxy forwards `/api` and sends `X-Forwarded-Host`/`-Proto`, so
callback URLs point at the dev server.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/auth/login?returnUrl=/trips` | Start sign-in and return to a local path |
| `POST` | `/api/auth/logout` | End the app session and the provider session |
| `GET` | `/api/auth/me` | Get the signed-in user's display name |

Unauthenticated API calls receive `401`, and the SPA then navigates to
`/api/auth/login`. A signed-in user without a valid owner id receives `403`.

On sign-in, the provider's issuer and subject are looked up in the `Users` table, and a
row is created for a new identity. The internal `Users.Id` is the `OwnerId` on all data.
It is stored in the session cookie, so it is resolved once per sign-in rather than on
every request. Moving to another provider therefore only means remapping issuer and
subject, never rewriting owned rows.

Locally, the provider is Keycloak. The planned hosted provider in Azure is Microsoft
Entra External ID, configured through the same `Authentication:Oidc:Authority`,
`ClientId`, and `ClientSecret` settings.

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
infra/
  main.bicep                    Azure resources for the deployment
src/
  TravelMemory.AppHost/         Aspire resource graph and local Keycloak realm
  TravelMemory.Domain/          Trips, photo imports, jobs, and time semantics
  TravelMemory.Persistence/     EF Core mappings and migrations
  TravelMemory.ServiceDefaults/ Health, discovery, and telemetry
  TravelMemory.Api/             Authentication, SAS, and Minimal API contracts
  TravelMemory.Worker/          Queue, EXIF, derivative, and cleanup pipeline
  TravelMemory.Web/             React/Vite PWA
tests/
  TravelMemory.Domain.Tests/    Fast domain tests without Docker
  TravelMemory.IntegrationTests/ API and worker tests against SQL Server and Azurite
  TravelMemory.EndToEndTests/   Sign-in and photo import through the whole Aspire graph
```

## Local data

SQL Server and Azurite use named Docker volumes. Keycloak has no volume: the realm and
its test users are imported again on every start, and their fixed user ids keep each
user mapped to the same owner. Stop the AppHost before resetting local data:

```powershell
aspire stop --non-interactive
docker volume ls
docker volume rm <exact-volume-name-from-Aspire>
```

> [!CAUTION]
> Use the exact volume name shown in the Aspire resource details. Do not use
> `docker volume prune`, because it can delete data belonging to other local projects.

## Deploy to Azure

The Azure deployment is designed to cost close to nothing for a rarely used personal app:
everything scales to zero or runs on a free offer, and nothing has a fixed monthly price.
The first request after an idle period is slow while the containers start and the
database resumes.

| Resource | Why it costs (almost) nothing |
|---|---|
| Container Apps (Consumption): API, worker, maintenance and migration jobs | Scale to zero; the monthly free grant covers light use |
| Azure SQL serverless database on the free offer | 100,000 vCore seconds and 32 GB a month, then it pauses instead of billing |
| Storage account (Blob and Queue) | Pay per use, a few cents at most |
| Log Analytics | Ingestion capped at 0.1 GB a day, inside the free allowance |
| Images on GitHub Container Registry | Free for public images, instead of Azure Container Registry |
| Microsoft Entra External ID | Free for the first 50,000 monthly active users |

The worker is woken by the queue and goes back to zero replicas when it is empty, so it
does not keep the database awake. A scheduled job runs its maintenance once a day, and the
deploy workflow runs migrations as a one-off job. A budget on the resource group emails
at 50 % and 100 % of the budget and when the forecast exceeds it. It is an alert, not a
cap.

`infra/main.bicep` describes all Azure resources. The **Deploy** workflow builds the
images, deploys the template, and applies migrations. It only runs when started by hand.

### One-time setup

Portal labels change now and then, so some names may differ slightly.

1. **Resource group.** In **Resource groups → Create**, create `rg-travel-memory` in your
   subscription, for example in `North Europe`. In **Subscriptions → your subscription →
   Resource providers**, register `Microsoft.App`, `Microsoft.OperationalInsights`,
   `Microsoft.Sql`, `Microsoft.Storage`, and `Microsoft.ManagedIdentity` if they are not
   registered yet.
2. **Deploy identity for GitHub Actions** (in your usual Microsoft Entra tenant):
   - In **App registrations → New registration**, create `travel-memory-github-deploy` as
     single tenant, without a redirect URI. Note its client id and tenant id.
   - In **Certificates & secrets → Federated credentials → Add credential**, choose
     *GitHub Actions deploying Azure resources*, organization `olrik-web`, repository
     `travel-memory`, and entity type *Environment* with the name `production`. No client
     secret is needed, and only the deploy job can use it.
   - In **rg-travel-memory → Access control (IAM)**, assign it `Contributor` and
     `Role Based Access Control Administrator`, so it can create resources and grant the
     app identity access to storage. Both roles apply to this resource group only.
3. **Sign-in with Microsoft Entra External ID:**
   - In **Microsoft Entra External ID → Create a tenant**, create an *External* tenant,
     for example named `Travel Memory` with a unique domain name. Link it to your
     subscription and `rg-travel-memory`.
   - In that tenant, in **App registrations → New registration**, create
     `Travel Memory web` for accounts in this organizational directory only. Note its
     client id.
   - In **Certificates & secrets**, create a client secret and copy its value right away.
   - In **External Identities → User flows**, create a sign-up and sign-in flow with *Email
     with password* that collects *Display Name*, and add `Travel Memory web` to it.
4. **GitHub.** In **Settings → Environments**, create `production` with these environment
   variables:

   | Variable | Value |
   |---|---|
   | `AZURE_CLIENT_ID` | Client id of `travel-memory-github-deploy` |
   | `AZURE_TENANT_ID` | Your usual tenant id |
   | `AZURE_SUBSCRIPTION_ID` | Your subscription id |
   | `OIDC_AUTHORITY` | `https://<external-domain>.ciamlogin.com/<external-tenant-id>/v2.0` |
   | `OIDC_CLIENT_ID` | Client id of `Travel Memory web` |
   | `BUDGET_ALERT_EMAIL` | Where budget alerts go |
   | `AZURE_LOCATION` | Optional region for all resources, for example `swedencentral` |

   Add the web app's client secret as the environment secret `OIDC_CLIENT_SECRET`.

   Without `AZURE_LOCATION`, resources go to the resource group's region. Popular regions
   sometimes stop accepting new SQL servers (`RegionDoesNotAllowProvisioning`); North
   Europe did in September 2026, and Sweden Central worked instead. To move, delete the
   resources inside the resource group, but not the group itself, because it holds the
   deploy identity's role assignments. Then set `AZURE_LOCATION` and deploy again.

### Deploy

1. In **Actions → Deploy**, choose **Run workflow**.
2. **After the first run only:**
   - New GitHub packages are private. In your profile's **Packages**, open
     `travel-memory-api` and `travel-memory-worker` and change their visibility to
     *Public* in **Package settings**, then run the workflow again. Container Apps pulls
     the images without credentials.
   - Add `<app-url>/api/auth/callback` and `<app-url>/api/auth/signed-out` as *Web*
     redirect URIs of `Travel Memory web`. Entra only returns to registered URLs, including
     after sign-out. The workflow summary shows the app URL.

The migration job may finish just after the new revision starts. With a single user and
scale to zero, that short window is accepted.

## Explicitly out of scope

GPX/FIT and GPS matching, SignalR, AI captioning/embeddings, weather, family sharing,
trip/photo editing and deletion, and offline trip data are not part of the implemented
slices.

For the recommended order of future work, see [ROADMAP.md](ROADMAP.md).
