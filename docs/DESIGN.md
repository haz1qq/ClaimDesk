# Claims Platform: Design

- **Status:** Draft for approval
- **Date:** 25 Nov 2026

## 1. Intent

This is a take-home assessment: build a claims platform for motor and property claims across six APAC markets. The panel cares about three things:

- the decisions made and how well they can be explained,
- how AI was directed, challenged and overridden,
- how much got built, and how well, in a 2–3 hour sprint (hard cap 5 hours).

Two people-related facts shape every decision below:

1. **The author must defend every line in a walkthrough.**
2. **Kafka, EF Core and Angular are all new to the author.** C# is where they are most confident.

So the design keeps the number of concepts small for each new technology, and puts the depth (state rules, validation, tests) in plain C#.

### Who uses it

| Role | Needs |
|---|---|
| **Claimant** | Report an incident, track a claim, answer information requests, receive a decision |
| **Claims officer** | Pick up incoming claims, assess them, request information, settle or reject them, and see the team's workload |
| **Manager** | See outstanding claims and liability exposure in real time |

All three roles use the same Angular app.

## 2. Goals, non-goals, anti-goals

**Goals**
- The full claim lifecycle works through the REST API, end to end.
- Kafka has a job that can be defended in one sentence.
- Liability exposure is modelled the way insurers do it: a **reserve** per open claim, summed per currency.
- One command (`docker compose up`) starts the infrastructure, and the README has three or four commands to start everything else.
- A meaningful commit history, plus an AI journal kept as we go.

**Non-goals (deliberately left out, and explained in the walkthrough)**
- **Real authentication.** A role switcher sends an `X-User-Id` header. A production system would use OIDC/JWT.
- **Policies and coverage checks.** A real system would check the claim against the policy admin system.
- **Payments.** "Settled" means a settlement was decided, not that money was paid.
- **Currency conversion.** Exposure is reported per currency and never summed across currencies.
- **Not built:** file uploads (photos, documents), fraud scoring, real email or SMS delivery, and localisation.
- **No reassigning or withdrawing claims.** Once an officer picks up a claim it stays with them, and claimants can't withdraw.
- **Notifications go to claimants only.** Officers see a claimant's reply by checking their claim list.

**Anti-goals (would count as failure even if it works)**
- Code the author can't explain, or patterns copied in because they "sound senior".
- A rushed attempt at the front end that weakens the backend.
- Any Chubb branding, logo, colours or proprietary material, or any public hosting.

## 3. Constraints

- **Backend:** .NET 10 (LTS), pinned with `global.json`. C#, ASP.NET Core minimal APIs.
- **Frontend:** Angular 21 (the CLI installed on this machine).
- **Messaging:** Kafka, run locally as a single broker in KRaft mode (no ZooKeeper) via Docker Compose.
- **Database:** PostgreSQL via Docker Compose. Data access is EF Core with the Npgsql provider. **Not SQLite:** the SQLite provider can't run `SUM` over `decimal` columns in the database, and exposure is a `SUM` over money.
- **Hosting:** local only.

## 4. Approaches considered

| | Approach | Trade-off | Verdict |
|---|---|---|---|
| A | **One service.** The Kafka consumer runs inside the API. | Simplest to run, but Kafka only carries messages from the API to itself, and "why Kafka?" has no good answer. | Rejected |
| **B** | **Claims API plus a Notifications service.** | Two processes, but the second is small. Kafka separates side effects from claim decisions. | **Chosen** |
| C | **One service per business step** (intake, assessment, settlement, notifications). | Every step changes the same claim, so it would need distributed consistency (sagas) across four databases. Far too much for the timebox and for a first project with Kafka. | Rejected |

**Why B:** it's the smallest design in which Kafka does a real job. The rule is:

> **Anything a user is waiting on goes over REST. Side effects that must never slow down or break a claim decision go through Kafka.**

If the Notifications service is down, claims still get decided. Its events wait in Kafka, and it catches up when it comes back.

## 5. Architecture

```
               ┌──────────────── REST (JSON) ─────────────────┐
               │                                              │
   Angular app ┤                                              ▼
   (port 4200) │                                  Notifications service (5200)
               ▼                                  - consumes claim-events
        Claims API (5100) ──── Postgres db: claims          - stores notifications
        - all claim reads/writes                  - GET /api/notifications
        - dashboard queries              ▲
        - outbox publisher ───► Kafka topic "claim-events" ──┘
                                (key = claimId)        Postgres db: notifications
```

- **The Claims API owns all claim data.** It is the only writer to the `claims` database.
- **The Notifications service owns its own `notifications` database** (same Postgres container, separate database). The two services never share tables.
- In development, the Angular dev server proxies `/api/claims*`, `/api/dashboard` and `/api/users` to port 5100, and `/api/notifications` to port 5200. Because everything appears to come from one origin, no CORS setup is needed.

## 6. Domain model (Claims API)

### Entities

**User** (seeded and read-only)

| Field | Notes |
|---|---|
| `Id` | |
| `Name` | |
| `Role` | `Claimant`, `Officer` or `Manager` |
| `Market` | Claimants only |

**Claim** (the aggregate: all changes go through its methods)

| Field | Notes |
|---|---|
| `Id` | `Guid` |
| `Number` | Human-readable, e.g. `CLM-000123` |
| `ClaimantId` | |
| `Market` | Copied from the claimant |
| `Currency` | Derived from the market |
| `Type` | `Motor` or `Property` |
| `IncidentDate` | Must not be in the future |
| `Description` | Required |
| `ClaimedAmount` | The claimant's estimate. Must be greater than 0. |
| `Reserve` | The insurer's current estimate of what the claim will cost. Set to `ClaimedAmount` at submission; the assigned officer can adjust it. |
| `SettlementAmount` | Only set when settled |
| `RejectionReason` | Only set when rejected |
| `Status` | See the state machine below |
| `AssignedOfficerId` | Null until picked up |
| `CreatedAt`, `UpdatedAt` | UTC |
| Concurrency token | Postgres `xmin`. A conflicting update returns **409**. |

**InformationRequest**

| Field | Notes |
|---|---|
| `Id`, `ClaimId` | |
| `Question` | |
| `Response` | Nullable |
| `RequestedBy` | |
| `RequestedAt`, `RespondedAt` | UTC |

**ClaimEvent** (does two jobs: the claim's **timeline** and the Kafka **outbox**)

| Field | Notes |
|---|---|
| `Id` | `Guid`. Also serves as the event ID that consumers use to drop duplicates. |
| `ClaimId` | |
| `Type` | |
| `ActorId` | |
| `OccurredAt` | |
| `Payload` | JSON |
| `PublishedAt` | Null until sent to Kafka |

### Markets

Market codes are generic and contain no proprietary data:

| Code | Market | Currency |
|---|---|---|
| SG | Singapore | SGD |
| MY | Malaysia | MYR |
| HK | Hong Kong | HKD |
| TH | Thailand | THB |
| ID | Indonesia | IDR |
| AU | Australia | AUD |

### State machine

```
Submitted ──assign──► UnderReview ──settle──► Settled
                          │  ▲   └──reject──► Rejected
               request    │  │  claimant
               info       ▼  │  responds
                    AwaitingInformation
```

| Action | Who | From | To | Rules |
|---|---|---|---|---|
| Submit | Claimant | (new) | Submitted | Validation as in the Claim table. Reserve = claimed amount. |
| Assign (pick up) | Officer | Submitted | UnderReview | Only one officer can win. A concurrent attempt gets **409**. |
| Request information | Assigned officer | UnderReview | AwaitingInformation | Question required. Only one open request at a time. |
| Respond | The claim's claimant | AwaitingInformation | UnderReview | Answers the open request |
| Adjust reserve | Assigned officer | UnderReview or AwaitingInformation | (unchanged) | Amount must be 0 or more |
| Settle | Assigned officer | UnderReview | Settled | Amount must be greater than 0. Reserve set to 0 (exposure released). |
| Reject | Assigned officer | UnderReview | Rejected | Reason required. Reserve set to 0. |

- **Invalid transitions** return **409** (for example, settling a rejected claim).
- **The wrong role or wrong person** gets **403**.
- **Every successful action** writes exactly one `ClaimEvent` **in the same transaction** as the claim change.

All the rules live in methods on the `Claim` class, in plain C# with no EF or HTTP dependencies, and are unit-tested. **This is where most of the depth goes.**

## 7. REST API (Claims API)

The **backend owns the contract** and publishes it as an OpenAPI document using .NET 10's built-in `Microsoft.AspNetCore.OpenApi`. The Angular side uses hand-written TypeScript interfaces that match it; generating them from the OpenAPI document is a stretch goal.

**Conventions:**
- Errors use the standard `ProblemDetails` format.
- Enums are serialised as strings.
- All times are UTC.
- The caller is identified by the `X-User-Id` header. A missing or unknown user gets **401**.

| Method | Path | Who | Purpose |
|---|---|---|---|
| GET | `/api/users` | anyone | Seeded users, for the role switcher |
| POST | `/api/claims` | Claimant | Submit a claim |
| GET | `/api/claims` | all | Claimant: their own claims. Officer: the unassigned queue plus their own. Manager: all. Optional `?status=` filter. |
| GET | `/api/claims/{id}` | owner, officer, manager | Claim detail with its timeline and information requests |
| POST | `/api/claims/{id}/assign` | Officer | Pick up the claim |
| POST | `/api/claims/{id}/information-requests` | Assigned officer | Ask a question |
| POST | `/api/claims/{id}/information-requests/{requestId}/response` | Claimant | Answer it |
| PUT | `/api/claims/{id}/reserve` | Assigned officer | Adjust the reserve |
| POST | `/api/claims/{id}/settle` | Assigned officer | Settle with an amount |
| POST | `/api/claims/{id}/reject` | Assigned officer | Reject with a reason |
| GET | `/api/dashboard` | Officer, Manager | Workload and exposure (below) |

**Dashboard contents**, each a plain query against the claims database:
- Open claims by status.
- Open claims per officer, plus the unassigned count.
- Liability exposure: the sum of `Reserve` over open claims, **per currency**, with the claim count.
- Per officer over the last 30 days: claims closed and average days from submission to decision.
- Ageing: open claims older than 7 and 30 days.

## 8. Kafka

**Topic `claim-events`:**
- one topic, 3 partitions, created by the Claims API at startup if it doesn't already exist (Kafka's auto-create would give it only 1 partition);
- message **key = claimId**, so all of one claim's events land on the same partition and stay in order;
- message value is JSON.

**Event schema:**
```json
{ "eventId": "guid", "type": "ClaimSubmitted", "claimId": "guid", "claimNumber": "CLM-000123",
  "claimantId": "guid", "occurredAt": "2026-11-25T10:00:00Z", "data": { } }
```

**Event types:**
- `ClaimSubmitted`
- `ClaimAssigned`
- `InformationRequested`
- `InformationProvided`
- `ReserveAdjusted`
- `ClaimSettled`
- `ClaimRejected`

The JSON shape is the contract. Each service has its own C# record for it, and the consumer only reads the fields it needs. There's no shared library, so the two services stay independently deployable.

**Producer: an outbox publisher in the Claims API.**
- A `BackgroundService` checks the `ClaimEvents` table about once a second for rows where `PublishedAt` is null, sends them to Kafka in order, then sets `PublishedAt`.
- **Why an outbox:** writing to the database and publishing to Kafka in two separate steps is unsafe. A crash between them leaves the claim updated but the event lost. Writing the event row in the same transaction as the claim change means an event is never lost.
- **The cost:** delivery is **at-least-once**. A crash after sending but before marking `PublishedAt` sends the event twice, so the consumer must handle duplicates.

**Consumer: the Notifications service.**
- Consumer group `notifications`, with auto-commit off: the offset is committed only after the event has been processed.
- **Duplicates:** it stores the `eventId`s it has processed, and a unique constraint on that column makes a repeated event a no-op.
- **What it acts on:** `ClaimSubmitted`, `ClaimAssigned`, `InformationRequested`, `ClaimSettled` and `ClaimRejected`. For each it creates a claimant-facing `Notification` (recipient, claim number, message, created at) and logs "email sent" to the console in place of a real email gateway. The other event types are ignored.
- **Endpoint:** `GET /api/notifications` returns the calling user's notifications, identified by `X-User-Id`.

## 9. Async across both tiers

- Claims take hours or days to process. The user waits for a REST response only for their own action (submitting, assigning, settling), and that response is synchronous and immediate.
- Everything downstream (notifications) arrives asynchronously.
- **The front end polls:** notifications every 10 seconds, and the claim list or detail view is refreshed after each action.
- Server-sent events (the server pushing updates to the browser) are a stretch goal. Polling is defensible at this scale and has far fewer moving parts.

## 10. Frontend (Angular)

- Standalone components, signals, `HttpClient`, and one API service per backend.
- An HTTP interceptor adds `X-User-Id`. The selected user is kept in `localStorage`.
- A neutral product name (working name **"ClaimDesk"**) with its own simple palette and no third-party branding.

**Pages:**

| Route | Role | Content |
|---|---|---|
| (header) | all | Role and user switcher; notifications badge |
| `/claims` | Claimant | My claims (status, number, amount) and a "Report an incident" form |
| `/claims/:id` | Claimant | Timeline, decision, and a form to answer an open question |
| `/work` | Officer | Unassigned queue (Pick up) and my claims |
| `/work/:id` | Officer | Detail with actions: request info, adjust reserve, settle, reject |
| `/dashboard` | Officer, Manager | Workload and exposure tables |

## 11. Testing

- **Unit tests (xUnit) on the `Claim` state machine:** every allowed transition, every rejected transition, the validation rules, and the reserve rules. This is the core, so it gets the most tests.
- **Stretch:** API integration tests with `WebApplicationFactory`, and a duplicate-event test for the consumer.

## 12. Running it locally

- `docker-compose.yml` runs Postgres (with both databases created on first start) and a single Kafka broker in KRaft mode.
- The services run with `dotnet run` and the front end with `ng serve`. The README lists the exact commands.
- EF Core **migrations** create the schema, and are applied automatically when each service starts. The Claims API seeds users and sample claims across several markets so the dashboard isn't empty.
- **Ports:**

| Service | Port |
|---|---|
| Claims API | 5100 |
| Notifications service | 5200 |
| Angular | 4200 |
| Postgres | 5432 |
| Kafka | 9092 |

- **Stretch:** containerise both services so a single `docker compose up` runs everything.

### Repository layout
```
/src/ClaimsApi                  ASP.NET Core minimal API + EF Core + outbox publisher
/src/NotificationsService       ASP.NET Core + Kafka consumer (BackgroundService) + EF Core
/tests/ClaimsApi.Tests          xUnit
/web                            Angular app
/docs                           DESIGN.md, AI_JOURNAL.md
docker-compose.yml, global.json, README.md, ClaimsPlatform.sln
```

## 13. Build order and scope

**Core (target about 3 hours, committing at each step):**
1. Repository skeleton: solution, `docker-compose.yml`, README stub.
2. `Claim` domain and state machine, with unit tests.
3. Claims API: EF Core context, migration, seed data, endpoints, error mapping.
4. Outbox publisher sending to Kafka.
5. Notifications service: consumer, duplicate handling, notifications endpoint.
6. Dashboard endpoint.
7. Angular: role switcher, claimant pages, officer pages.

**Stretch (hours 4–5, in this order):**
1. Dashboard page.
2. Notifications badge.
3. Containerised services.
4. API integration tests.
5. Server-sent events in place of polling.
6. TypeScript client generated from the OpenAPI document.

**Cut list if time runs short (cut from the bottom up):** officer pages, then claimant pages, then dashboard metrics beyond exposure and workload. The backend lifecycle, the outbox and the consumer are never cut.

## 14. The brief's questions, answered

| Question in the brief | Answer |
|---|---|
| Core entities? | Claim (the aggregate), InformationRequest, ClaimEvent, User, plus Notification in the second service. See §6. |
| One service or more? | Two. Claims stays whole because every step changes the same claim in one transaction. Notifications is split out because it only reacts and must never block a decision. See §4. |
| Sync or event-driven? | REST for anything a user waits on. Kafka for side effects. See §4 and §8. |
| Frontend/backend contract? | The backend owns it and publishes it as OpenAPI. See §7. |
| Async across tiers? | Outbox, then Kafka, then an idempotent consumer. The UI polls. See §8 and §9. |
| What an officer needs to see | Unassigned queue, their own claims, ageing, and team workload. See §7 and §10. |
| Liability exposure | The reserve on each open claim, summed per currency, set to 0 when the claim is closed. See §6 and §7. |

## 15. Left to the builder

- Exact NuGet and npm package versions, and the Kafka image tag (pin one 4.x tag).
- How the claim number is generated (an identity column or a Postgres sequence).
- Folder structure inside each project, and naming within the conventions above.
- The visual styling of the Angular app, within "neutral, no third-party branding".
