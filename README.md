# ClaimDesk

A local claims platform for motor and property claims. Claimants report and track claims; claims officers assess, settle or reject them; managers see workload and liability exposure.

- **Design:** [docs/DESIGN.md](docs/DESIGN.md)
- **Implementation plan:** [docs/PLAN.md](docs/PLAN.md)
- **AI working journal:** [docs/AI_JOURNAL.md](docs/AI_JOURNAL.md)

## Prerequisites

- .NET SDK 10 (pinned in `global.json`)
- Docker Desktop (or Docker Engine with Compose)
- Node.js 22 and npm (for the Angular app)

## Run it

```bash
docker compose up -d                              # Postgres + Kafka
dotnet tool restore                               # dotnet-ef (only needed to add migrations)
dotnet run --project src/ClaimsApi                # http://localhost:5100
dotnet run --project src/NotificationsService     # http://localhost:5200
cd web && npm install && npm start                # http://localhost:4200
```
