# ClaimDesk

A local claims platform for motor and property claims. Claimants report and track claims; claims officers assess, settle or reject them; managers see workload and liability exposure.

- **Design:** [docs/DESIGN.md](docs/DESIGN.md)
- **Implementation plan:** [docs/PLAN.md](docs/PLAN.md)
- **AI working journal:** [docs/AI_JOURNAL.md](docs/AI_JOURNAL.md)

## Architecture in one paragraph

**Claims API** (.NET 10, EF Core, Postgres) owns every claim and serves the Angular app over REST. Every claim change also writes an event row in the same transaction (the **outbox**); a background publisher sends those rows to the Kafka topic `claim-events`. The **Notifications service** consumes that topic idempotently and stores claimant notifications. In short: REST for anything a user waits on, Kafka for side effects that must never block or break a claim decision.

## Prerequisites

- .NET SDK 10 (pinned in `global.json`)
- Docker Desktop (or Docker Engine with Compose)
- Node.js 22+ and npm

## Run it

```bash
docker compose up -d                              # Postgres + Kafka
dotnet run --project src/ClaimsApi                # http://localhost:5100 (applies migrations, seeds demo data)
dotnet run --project src/NotificationsService     # http://localhost:5200
cd web && npm install && npm start                # http://localhost:4200
```

Open http://localhost:4200 and pick a user from **Signed in as**.

| User | Role | Notes |
|---|---|---|
| Aisha Rahman | Claimant | Malaysia (MYR) |
| Daniel Tan | Claimant | Singapore (SGD) |
| Mei Lin | Claims officer | |
| Arjun Patel | Claims officer | |
| James Lee | Manager | Dashboard |

To reset to clean demo data: `docker compose down -v && docker compose up -d`, then restart both services.

## Tests (xUnit)

```bash
dotnet test --filter "Category!=Smoke"   # unit tests: claim rules, notification text (no infrastructure needed)
dotnet test --filter Category=Smoke      # end-to-end: needs compose + both services running
```

On Windows, stop the running services before rebuilding, or build first and use `dotnet test --no-build` (a running .exe locks its own files).

## Known limitations (deliberate, see DESIGN.md §2)

- **No real authentication.** The UI sends an `X-User-Id` header. Both services refuse to start outside the Development environment; production would use OIDC/JWT.
- **One team across markets.** Any officer can view any claim; only the assigned officer can act on it. There is no per-market scoping.
- **No reassignment, withdrawal, payments, policy or coverage checks, file uploads, or real email.** "Email sent" is a log line in the Notifications service.
- **Exposure is per currency, never converted.**
- **Polling, not push.** Pages refresh every 10 seconds.
- **Transient failures in the consumer retry without a cap** (by design, so no event is dropped during an outage). There is no dead-letter topic.
