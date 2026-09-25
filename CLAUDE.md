# Travel Memory

A private travel-memory app (trips, photos, and later tracks and notes on one timeline).
It is a personal learning project, not an enterprise product: prefer the simplest design
that is correct, and don't add abstractions, layers, or infrastructure "for later".
Product scope and planned work live in `ROADMAP.md`; setup and API details in `README.md`.

## Stack

.NET 10 (Minimal API, EF Core, SQL Server) orchestrated by Aspire, a Queue-driven worker
on Azurite Blob/Queue storage, and a React 19 + TypeScript + Vite PWA.

## Commands

```sh
dotnet build
dotnet test tests/TravelMemory.Api.Tests    # needs Docker (SQL Server + Azurite containers)
npm --prefix src/TravelMemory.Web run lint
npm --prefix src/TravelMemory.Web run typecheck
npm --prefix src/TravelMemory.Web test
```

Warnings are errors in .NET. Add EF Core migrations with the local `dotnet-ef` tool;
never hand-edit generated migrations or the model snapshot.

## Architecture

Organize by feature (vertical slices), not by technical layer.

- Backend: shared rules live in `TravelMemory.Domain`, mappings/migrations in
  `TravelMemory.Persistence`. Each API use case is its own file in
  `TravelMemory.Api/Features/<Feature>/` (see `Features/Trips/CreateTrip.cs`); the
  `*Endpoints.cs` file only maps routes.
- Frontend: `src/features/<feature>/` owns its pages, hooks, API client, and types.
  Pages and components render and wire up events; state, side effects, polling, and
  storage go in hooks, and pure logic in plain `.ts` modules that are unit-tested
  without React.
- Every query is scoped to the current owner (`ICurrentUser.OwnerId`).

## Code style

- Everything in the codebase is English: identifiers, comments, UI text, commits, and docs.
- Comment the why, not the what. Explain non-obvious invariants, ordering,
  idempotency, concurrency, and time semantics, especially in services and the worker.
  Don't comment code that already reads clearly.

## Git

- Never push to `main`; it is protected. Work on a branch and open a PR, which is
  squash-merged.
- Bugs, refactors, and chores are tracked as GitHub issues. Keep one issue per PR and
  reference it in the description (`Closes #N`).
- Commit as `olrik-web <molrik@outlook.com>` (set `git config user.name` and
  `user.email` before the first commit).
- No `Co-Authored-By`, `Claude-Session`, or other AI attribution in commits, and no
  "Generated with Claude Code" footer in PR descriptions.
- Commit messages use gitmoji + Conventional Commits, imperative and lowercase, subject
  under ~60 characters: `✨ feat(trips): add end date validation`.
  ✨ feat · 🐛 fix · ♻️ refactor · ⚡️ perf · ✅ test · 📝 docs · 🎨 style ·
  🔧 chore/config · 👷 ci · 📦️ build · 🔒️ security · ⏪️ revert
- PR titles use the same format, since the title becomes the squash commit on `main`.
