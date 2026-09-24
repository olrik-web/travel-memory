# Travel Memory roadmap

This roadmap describes a sensible progression from the implemented local vertical slices.
It is a planning aid, not a commitment to every long-term idea. The current setup and
development commands are documented in [README.md](README.md).

Travel Memory is a personal learning project. Every milestone therefore states both the
product outcome and the .NET/web skills it is meant to practice. When two orderings are
equally valuable for the product, prefer the one that teaches the more transferable skill
first.

## Product vision and non-goals

Travel Memory is a private digital memory of where a person went, what happened, and how
the trip felt. It should help reconstruct and revisit a journey without becoming a travel
planner, a general-purpose photo manager, or a social network.

The product should remain:

- memory-first: trips, events, photos, tracks, notes, and context form one timeline
- private by default: data is owner-scoped and sharing must always be explicit
- explainable: imported and derived facts retain source, provenance, and confidence
- reversible: users can correct derived data and export or delete their information
- local-development friendly: production services should have practical local equivalents

## Cost-conscious principles

- Prefer Azure consumption/free tiers and local emulators until usage proves a need to
  scale.
- Do not retain photo originals by default; store only verified web derivatives and
  metadata.
- Cache external lookups such as weather and geocoding with source and attribution.
- Make AI optional, asynchronous, budgeted, and replaceable. Do not put AI on critical
  import paths.
- Minimize storage duplication, background compute, network egress, and always-on
  infrastructure.
- Measure real bottlenecks before adding brokers, caches, vector indexes, or real-time
  infrastructure.

## Status legend

| Status | Meaning |
|---|---|
| **Complete** | Implemented and validated locally |
| **Recommended next** | Best current candidate for the next vertical slice |
| **Candidate** | Sensible follow-on work; sequence may change after learning |
| **Exploratory** | Idea bank only; not approved scope |

Concrete bugs, refactorings, and chores are tracked as GitHub issues. This roadmap only
references them where they belong to a milestone.

## Progress overview

| Milestone | Status | Outcome | Learning focus |
|---|---|---|---|
| Foundation / V1 | **Complete** | A local Aspire application can create, list, and open owner-scoped trips | Aspire, Minimal API, EF Core, React/Vite |
| V2 photo import | **Complete** | Robust JPEG/HEIC import produces a corrected chronological photo timeline | Blob SAS, queues, background workers, idempotency |
| V2.5 hardening and foundations | **Recommended next** | The worker is robust, CI guards every change, a real identity provider is used, and the app runs in Azure | Resilient workers, CI, OIDC auth, Azure deployment |
| V3a GPX tracks on a map | **Candidate** | Import GPX tracks and show them on a trip map | Streaming parsing, spatial data, map rendering |
| V3b photo positioning | **Candidate** | Explainably match photos to track positions with manual correction | Time/timezone modelling, provenance, domain logic |
| V3c FIT import | **Candidate** | Import FIT files through the same track pipeline | Binary formats, pipeline extensibility |
| Trip and photo editing and deletion | **Candidate** | Edit trips, delete photos and trips with verifiable blob cleanup | Consistency across SQL and storage, cancellation |
| Richer memories and context | **Candidate** | Notes, timeline events, weather, gap detection, and editable summaries | Event modelling, external APIs, caching |
| AI-assisted recall and search | **Candidate** | Optional, provenance-aware generation and search with strict cost controls | Microsoft.Extensions.AI, embeddings, SQL vector |
| Privacy and production hardening | **Candidate** | Export, backups, alerting, and recovery procedures | Operations, observability, lifecycle policies |

## Completed: Foundation / V1

- .NET Aspire AppHost with SQL Server, Azurite, API, web resource wiring, and
  ServiceDefaults
- React, TypeScript, and Vite PWA with Oxlint and type checking
- ASP.NET Core Minimal API on .NET 10
- EF Core migrations and persistent SQL Server storage
- a Development-only identity behind an authentication and `OwnerId` boundary
- create, list, and open Trip flows
- domain, API, SQL-backed integration, and frontend tests; the distributed Aspire flow
  is validated manually until an `Aspire.Hosting.Testing` test exists (see V2.5)

## Completed: V2 robust photo import

- direct private Blob upload through short-lived, per-file Create/Write SAS tokens
- bounded browser concurrency, resumable batches, stable file identities, polling, and
  actionable per-file status
- JPEG and HEIC decoding from the first photo slice
- EXIF capture-time extraction and orientation normalization
- batch-level time-adjustment preview while preserving original EXIF time and metadata
- owner/trip-scoped SHA-256 duplicate detection
- Azure Storage Queue worker with explicit jobs, retries, idempotency, recovery, and
  visible failure state
- verified web JPEG and thumbnail derivatives
- original deletion only after both derivatives and metadata are safely persisted
- safe retention for failed originals and cleanup retries
- chronological owner-scoped photo timeline

## Recommended next: V2.5 hardening and foundations

### Intended outcome

The existing slices are robust enough to build on, every change is verified automatically,
the Development-only identity is replaced by a real identity provider, and the application
can be deployed to Azure. This comes before V3 because authentication, CI, and deployment
are the most transferable .NET skills and get harder to retrofit as the data model grows.

### Proposed scope

- Fix the known worker and API bugs: unhandled exceptions stopping the worker host,
  poison messages without a dequeue limit, duplicate redispatch messages, the in-process
  duplicate lock, and concurrent upload completion returning 500.
- Propagate trace context from the API through the queue into the worker so an import is
  one distributed trace in the Aspire dashboard.
- Add a GitHub Actions workflow that builds, lints, type-checks, and runs the .NET and
  frontend tests on every push.
- Add one end-to-end test with `Aspire.Hosting.Testing` that runs the real resource graph.
- Replace the Development authentication handler with OIDC. Use Keycloak through the Aspire
  integration locally and an equivalent hosted provider in Azure. Keep the `OwnerId`
  boundary unchanged.
- Deploy to Azure Container Apps Consumption, Azure SQL, Blob Storage, and Storage Queue
  with `aspire deploy` or `azd`. Use HTTPS-only SAS tokens and managed identity outside
  Development.
- Define how migrations run outside Development.

### Exit criteria

- The worker survives any single failing job or malformed message, and the failure is
  visible on the job and the import item.
- CI is green and required before merging to `main`.
- A user signs in through OIDC locally and in Azure, and all data remains owner-scoped.
- The deployed app can create a trip and import photos, and scales to zero when idle.
- A documented monthly cost budget and alert exist.

### Explicit V2.5 non-goals

- multi-user sharing, invitations, or family groups
- account recovery and self-service sign-up flows beyond what the identity provider offers
- backup/restore drills and production alerting beyond a cost alert
- custom domains or CDN

## Candidate: V3 tracks and photo positioning

### Intended outcome

A user can import one or more GPX or FIT files into a trip, review normalized tracks, and
see photos positioned from the best available track evidence. Every derived position is
explainable and manually correctable.

V3 is deliberately split into three independently useful increments, so each one can be
completed, used, and learned from before the next starts.

### V3a: GPX tracks on a map

- Import GPX without routing large files through unnecessary in-memory buffers.
- Preserve the raw import as temporary/reprocessable input while storing normalized track
  points and source metadata.
- Normalize timestamps to an explicit instant model and retain the source timezone/offset
  evidence.
- Evaluate SQL Server `geography` with NetTopologySuite in EF Core for spatial storage.
- Produce downsampled track representations for map display while retaining sufficient
  resolution for photo matching.
- Show the track on a trip map (for example MapLibre GL) with an acceptable tile source.

### V3b: photo positioning

- Extend the existing camera-time workflow so users can reconcile EXIF wall-clock time,
  batch adjustment, timezone assumptions, and track instants before matching.
- Match photos to interpolated track positions by corrected capture time.
- Persist matching method, source track/segment, time distance, confidence, and provenance
  for every derived photo position.
- Show positioned photos, unmatched photos, and low-confidence matches on the map and
  timeline.
- Allow manual correction, removal, or confirmation of a derived photo position without
  overwriting the imported evidence.

### V3c: FIT import

- Import FIT files through the same idempotent track pipeline as GPX.
- Evaluate maintained .NET FIT parsers, licenses, and Linux compatibility through a spike.

### Exit criteria

- Representative GPX and FIT fixtures import through one idempotent pipeline.
- Track normalization and downsampling are deterministic and covered by tests.
- Timezone and camera-time assumptions are visible before matching.
- Photos inside track coverage receive reproducible positions with confidence and
  provenance.
- Photos outside coverage or beyond a documented threshold remain explicitly unmatched.
- A user can correct a match and the correction survives reprocessing.
- The trip map and timeline remain useful when only tracks, only photos, or partial matches
  are available.
- The full flow runs through Aspire with SQL Server and local storage.

### Explicit V3 non-goals

- route planning, navigation, or live location tracking
- direct Garmin, Google, or Apple account integrations
- background mobile GPS capture or offline synchronization
- AI-based place, activity, or route inference
- automatic publication or sharing of location data
- multi-user collaboration or real-time SignalR updates
- advanced cartography, 3D terrain, or a general GIS editing experience

## Subsequent milestone candidates

These are deliberately outcome-oriented candidates, not a fixed task backlog.

### Trip and photo editing and deletion

Allow editing trip title and dates, deleting individual photos, and deleting a whole trip.
Deletion must remove derivatives and metadata verifiably, cancel or complete pending jobs
safely, and never leave orphaned blobs. This can be done before, between, or after the V3
increments, but should come before real daily use.

### Notes and richer timeline events

Add concise notes, manually created memories, and a shared timeline event model that can
combine photos, positions, notes, and future contextual facts without flattening their
provenance.

### Historical weather context

Use Open-Meteo for historical weather only after position/time quality is sufficient.
Store attribution, request inputs, cache metadata, source timestamps, and confidence.
Avoid repeated external calls for unchanged trip segments.

### Heuristic gap detection before AI

Detect likely missing periods, unusually long stops, rapid movement, or clusters using
deterministic rules first. Let users dismiss or correct suggestions. Collect evidence that
AI would add value before introducing it.

### Editable summaries and journal regeneration

Generate a basic trip outline from confirmed timeline events, then support user-authored
summaries and journal editing. Regeneration must never overwrite manual edits silently;
generated sections need version and source provenance.

### Optional AI provider abstraction

Introduce `Microsoft.Extensions.AI` only when a concrete assisted-recall workflow is
approved. Use Ollama locally and OpenAI in production behind the same application
abstraction. Enforce per-operation budgets, explicit opt-in, prompt/model/version
provenance, retry limits, and observable token/cost usage.

### Semantic search only when justified

Start with structured filters and full-text search. Add embeddings and native SQL vector
support only if real queries cannot be served well otherwise. Keep embedding model,
dimensions, source text, generated-at time, and re-index version explicit.

### Export and privacy controls

Provide portable export of trips, timeline data, tracks, metadata, and derivative media.
Add understandable deletion scopes, retention visibility, and verifiable cleanup without
requiring the hosted application to read retained originals.

### Production hardening, backup, and monitoring

Building on the V2.5 deployment, define backup/restore drills, storage lifecycle policies,
health alerts, structured logs, trace retention, and recovery procedures before calling
the service production-ready.

## Longer-term possibilities: exploratory idea bank

The following items are intentionally **exploratory**, not promised milestones:

- account recovery and self-service sign-up beyond the identity provider's defaults
- multi-user ownership, invitations, and small family groups
- trip collaboration with explicit view/edit/share permissions
- optional, user-controlled original-photo retention
- offline capture, queued edits, and conflict-aware synchronization
- Garmin, Google, or Apple imports only after core import value is proven
- richer maps, places, and carefully attributed geocoding
- activity inference with visible evidence and manual correction
- journal layouts, PDF export, or static-site export
- self-hosting and a separately deployable local processing worker

## Architecture guardrails

- Keep `OwnerId` on every relevant aggregate and apply owner filtering at every API/data
  boundary.
- Treat raw imported facts, user corrections, and derived values as distinct data with
  provenance and confidence.
- Do not retain photo originals by default. Delete temporary originals only after verified
  derivative and metadata persistence.
- Keep large uploads direct to Blob Storage through short-lived, least-privilege tokens.
- Make every import and background pipeline idempotent, resumable, observable, and safe to
  retry. A single failing job must never stop a worker process.
- Keep SQL/database jobs authoritative; queue messages are wake-up signals, not the only
  record of work.
- Do not introduce Service Bus, SignalR, vector search, or additional distributed
  infrastructure before a measured requirement exists.
- Preserve local/cloud portability through Aspire resource references and standard Azure
  services.
- Design export and deletion alongside new data models so user data never becomes trapped
  in derived-only formats.
- Keep external-source attribution, cache policy, model version, prompt version, and cost
  evidence wherever those concepts apply.

## Next session: start V2.5

- Create GitHub issues for the known bugs, refactorings, and chores from the first code
  review, labelled `bug`, `refactor`, `enhancement`, `test`, or `chore`.
- Fix the worker robustness bugs first, each with a regression test.
- Add the GitHub Actions CI workflow so later changes are verified automatically.
- Spike Keycloak through Aspire and decide the hosted identity provider for Azure.
- Spike `aspire deploy`/`azd` to Azure Container Apps and document the cost budget.
