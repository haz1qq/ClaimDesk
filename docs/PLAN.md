# ClaimDesk Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

- **Goal:** A local claims platform. A Claims API owns the claim lifecycle and publishes events through an outbox to Kafka. A Notifications service consumes those events. A thin Angular front end sits on top.
- **Architecture:** two ASP.NET Core (.NET 10) services, each with its own Postgres database via EF Core, connected by the Kafka topic `claim-events`. REST for anything a user waits on, Kafka for side effects. Angular 21 talks to both services through the dev-server proxy.
- **Tech stack:**
  - .NET 10 SDK, ASP.NET Core minimal APIs, `Microsoft.AspNetCore.OpenApi`
  - EF Core 10 with `Npgsql.EntityFrameworkCore.PostgreSQL`
  - `Confluent.Kafka` 2.x
  - xUnit
  - Postgres 17, `apache/kafka` 4.x in KRaft mode
  - Angular 21
- **Spec:** `docs/DESIGN.md`. Read it alongside this plan; section numbers below (§N) refer to it.

## Global Constraints

**Platform**
- SDK pinned by `global.json` to `10.0.100` with `rollForward: latestFeature`. All C# projects target `net10.0`.
- **Ports:**

| Service | Port |
|---|---|
| Claims API | 5100 |
| Notifications service | 5200 |
| Angular | 4200 |
| Postgres | 5432 |
| Kafka | 9092 |

- **Databases:** `claims` and `notifications` in one Postgres container. User and password are both `postgres` (local development only).

**Kafka**
- Topic `claim-events`, 3 partitions, message key = claim ID (as a string), JSON value.
- Consumer group `notifications`.

**API conventions**
- Errors use `ProblemDetails`. Enums are serialised as strings. All times are UTC `DateTimeOffset` values written to `timestamptz` columns (Npgsql rejects non-UTC offsets).
- Identity comes from the `X-User-Id` header. A missing or unknown user gets **401**.
- Status codes: a rule violation is **400**, the wrong role or wrong person is **403**, an invalid transition or concurrency conflict is **409**.

**Product rules**
- Market to currency: SG→SGD, MY→MYR, HK→HKD, TH→THB, ID→IDR, AU→AUD.
- Claim number is shown as `CLM-` followed by the number zero-padded to 6 digits, e.g. `CLM-000123`.
- **No Chubb name, logo or colours anywhere.** The product name is "ClaimDesk". Nothing is hosted publicly.

**Process**
- Commit at the end of every task. Every commit message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Add an entry to `docs/AI_JOURNAL.md` whenever the user accepts, challenges or overrides something. Never write the user's reasons for them.

## Review Focus

These are failure modes the spec implies but unit tests don't reach. Each is pinned by a smoke test (Task 4) or a verification step (Tasks 5 and 6).

1. **Two officers pick up the same claim at once.** Exactly one gets 200 and the other gets 409, never two owners. Pinned in Task 4 (`Concurrent_assign_has_one_winner`).
2. **Kafka is down when a claim changes.** The API still returns 200, and the event is published once Kafka is back. Pinned in Task 5, Step 4.
3. **An event is delivered twice** (redelivery or offset reset). No duplicate notification is created. Pinned in Task 6, Step 6.
4. **Missing or unknown `X-User-Id`, or a claimant opening someone else's claim.** These return 401 or 403, never 500 and never the data. Pinned in Task 4 (`Unknown_user_is_401`, `Other_claimant_is_403`).
5. **Malformed input** (bad JSON, an unknown enum value such as `"Type":"Boat"`, a missing body). Returns 400 `ProblemDetails`, not 500. Pinned in Task 4 (`Unknown_enum_is_400`).

---

## File map

```
global.json                        (exists)
ClaimsPlatform.slnx                .NET 10 default solution format
.config/dotnet-tools.json          local tool manifest: dotnet-ef
docker-compose.yml
infra/postgres/init.sql            CREATE DATABASE notifications;
README.md
src/ClaimsApi/
  Program.cs                       composition root only
  Domain/Enums.cs                  Market, ClaimType, ClaimStatus, Role, ClaimEventType, RuleViolation
  Domain/Markets.cs                Market -> currency
  Domain/ClaimRuleException.cs
  Domain/User.cs
  Domain/Claim.cs                  aggregate + state machine
  Domain/InformationRequest.cs
  Domain/ClaimEvent.cs             timeline row + outbox row
  Data/ClaimsDbContext.cs
  Data/SeedData.cs
  Data/Migrations/                 generated
  Api/CurrentUser.cs               X-User-Id endpoint filter + HttpContext extension
  Api/ErrorHandler.cs              IExceptionHandler -> ProblemDetails
  Api/Dtos.cs
  Api/UserEndpoints.cs
  Api/ClaimEndpoints.cs
  Api/DashboardEndpoints.cs
  Messaging/OutboxPublisher.cs     BackgroundService
src/NotificationsService/
  Program.cs
  ClaimEventMessage.cs             consumer's own view of the event
  NotificationText.cs              pure: event -> message text or null
  Notification.cs, ProcessedEvent.cs
  NotificationsDbContext.cs
  Migrations/                      generated
  ClaimEventsConsumer.cs           BackgroundService
tests/ClaimsPlatform.Tests/        one xUnit project referencing both services
  Domain/ClaimTests.cs
  Notifications/NotificationTextTests.cs
  Smoke/LifecycleSmokeTests.cs     [Trait("Category","Smoke")], calls the running API over HTTP
web/                               Angular app (Tasks 8–9)
```

Deviation from §12: the design names `/tests/ClaimsApi.Tests`. This plan uses a single `tests/ClaimsPlatform.Tests` project because the Notifications service also gets unit tests, and one test project is less to run and explain.

---

### Task 1: Skeleton and infrastructure

**Files:**
- Create: `ClaimsPlatform.slnx`, `src/ClaimsApi/*`, `src/NotificationsService/*`, `tests/ClaimsPlatform.Tests/*`, `.config/dotnet-tools.json`, `docker-compose.yml`, `infra/postgres/init.sql`, `README.md`

**Interfaces:**
- Produces: projects that build, a running Postgres with `claims` and `notifications` databases, and a Kafka broker on `localhost:9092`.

- [ ] **Step 1: Scaffold the .NET projects**

```bash
dotnet new sln -n ClaimsPlatform
dotnet new webapi -n ClaimsApi -o src/ClaimsApi
dotnet new webapi -n NotificationsService -o src/NotificationsService
dotnet new xunit -n ClaimsPlatform.Tests -o tests/ClaimsPlatform.Tests
dotnet sln add src/ClaimsApi src/NotificationsService tests/ClaimsPlatform.Tests
dotnet add tests/ClaimsPlatform.Tests reference src/ClaimsApi src/NotificationsService
dotnet new tool-manifest && dotnet tool install dotnet-ef
```

Delete the template's WeatherForecast endpoint and record type. Set `applicationUrl` in each `Properties/launchSettings.json` "http" profile to `http://localhost:5100` (ClaimsApi) and `http://localhost:5200` (NotificationsService).

- [ ] **Step 2: Add the packages**
  - ClaimsApi: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`, `Confluent.Kafka`
  - NotificationsService: the same three
  - Tests: nothing beyond the xUnit template's packages

- [ ] **Step 3: Write `docker-compose.yml`**
  - **Postgres:** service `postgres`, image `postgres:17`, environment `POSTGRES_USER=postgres`, `POSTGRES_PASSWORD=postgres`, `POSTGRES_DB=claims`, port `5432:5432`. Mount `./infra/postgres/init.sql` to `/docker-entrypoint-initdb.d/init.sql`. Health check `pg_isready -U postgres`.
  - **Kafka:** service `kafka`, image `apache/kafka:<pinned 4.x tag>`, port `9092:9092`, no `KAFKA_*` environment variables. The image's built-in single-node KRaft config advertises `localhost:9092`.

- [ ] **Step 4: Write the README stub:** prerequisites and the commands `docker compose up -d`, `dotnet run --project src/ClaimsApi`, `dotnet run --project src/NotificationsService`, `cd web && npm start`.

- [ ] **Step 5: Verify**

Run: `dotnet build` → Expected: `Build succeeded`, 0 errors.

Run: `docker compose up -d && docker compose ps` → Expected: `postgres` is healthy and `kafka` is running.

Run: `docker compose exec postgres psql -U postgres -lqt | cut -d'|' -f1` → Expected: the list includes `claims` and `notifications`.

Run: `docker compose exec kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list` → Expected: exits 0 (an empty list is fine).

- [ ] **Step 6: Commit** with `chore: scaffold solution, docker-compose for Postgres and Kafka`.

---

### Task 2: Claim domain and state machine (test first)

**Files:**
- Create: `src/ClaimsApi/Domain/{Enums,Markets,ClaimRuleException,User,Claim,InformationRequest,ClaimEvent}.cs`
- Test: `tests/ClaimsPlatform.Tests/Domain/ClaimTests.cs`

**Interfaces:**
- Produces (used by Tasks 3–7):

```csharp
public enum Market { SG, MY, HK, TH, ID, AU }
public enum ClaimType { Motor, Property }
public enum ClaimStatus { Submitted, UnderReview, AwaitingInformation, Settled, Rejected }
public enum Role { Claimant, Officer, Manager }
public enum ClaimEventType { ClaimSubmitted, ClaimAssigned, InformationRequested, InformationProvided, ReserveAdjusted, ClaimSettled, ClaimRejected }
public enum RuleViolation { Validation, Forbidden, Conflict }

public static class Markets { public static string CurrencyFor(Market m); }
public sealed class ClaimRuleException(RuleViolation kind, string message) : Exception(message) { public RuleViolation Kind { get; } }

public sealed class User { Guid Id; string Name; Role Role; Market? Market; }   // public get, private set

public sealed class Claim {
  Guid Id; int Number; string DisplayNumber /* "CLM-" + Number:D6 */; Guid ClaimantId; Market Market; string Currency;
  ClaimType Type; DateOnly IncidentDate; string Description; decimal ClaimedAmount; decimal Reserve;
  decimal? SettlementAmount; string? RejectionReason; ClaimStatus Status; Guid? AssignedOfficerId;
  DateTimeOffset CreatedAt; DateTimeOffset UpdatedAt; DateTimeOffset? DecidedAt; uint Version;
  List<InformationRequest> InformationRequests; List<ClaimEvent> Events;
  bool IsOpen;  // Status is Submitted, UnderReview or AwaitingInformation

  static Claim Submit(Guid claimantId, Market market, ClaimType type, DateOnly incidentDate, string description, decimal claimedAmount, DateTimeOffset now);
  void Assign(Guid officerId, DateTimeOffset now);
  InformationRequest RequestInformation(Guid officerId, string question, DateTimeOffset now);
  void RespondToInformationRequest(Guid claimantId, Guid requestId, string response, DateTimeOffset now);
  void AdjustReserve(Guid officerId, decimal newReserve, DateTimeOffset now);
  void Settle(Guid officerId, decimal amount, DateTimeOffset now);
  void Reject(Guid officerId, string reason, DateTimeOffset now);
}

public sealed class InformationRequest { Guid Id; Guid ClaimId; string Question; string? Response; Guid RequestedBy; DateTimeOffset RequestedAt; DateTimeOffset? RespondedAt; }

public sealed class ClaimEvent {
  Guid Id; Guid ClaimId; ClaimEventType Type; Guid ActorId; DateTimeOffset OccurredAt; string Payload; DateTimeOffset? PublishedAt;
  internal static ClaimEvent Create(Guid claimId, ClaimEventType type, Guid actorId, object data, DateTimeOffset now); // Payload = JsonSerializer.Serialize(data, JsonSerializerOptions.Web)
}
```

**Event `data` payloads** (camelCase, written by each `Claim` method):

| Event | Payload |
|---|---|
| ClaimSubmitted | `{ type, market, currency, claimedAmount }` |
| ClaimAssigned | `{ officerId }` |
| InformationRequested | `{ requestId, question }` |
| InformationProvided | `{ requestId }` |
| ReserveAdjusted | `{ previous, current, currency }` |
| ClaimSettled | `{ amount, currency }` |
| ClaimRejected | `{ reason }` |

Every method sets `UpdatedAt = now` and appends **exactly one** `ClaimEvent`. `Settle` and `Reject` also set `DecidedAt = now` and `Reserve = 0`. The `Claim` class has no EF Core or ASP.NET dependencies. EF Core reaches the private setters through mapping (Task 3) and a private parameterless constructor.

- [ ] **Step 1: Write the failing tests.** Helpers: `Now = 2026-11-26T00:00:00Z`; `NewClaim()` = `Claim.Submit(claimantA, Market.MY, ClaimType.Motor, new DateOnly(2026,11,20), "Rear-ended at lights", 5000m, Now)`; `UnderReview()` = `NewClaim()` then `Assign(officer1, Now)`. Each test asserts the event as `claim.Events.Last().Type`.

| Test | Act | Assert |
|---|---|---|
| `Submit_sets_initial_state` | `NewClaim()` | Status `Submitted`, Currency `"MYR"`, Reserve `5000m`, AssignedOfficerId null, one event `ClaimSubmitted` |
| `Submit_rejects_non_positive_amount` (Theory: 0, -1) | Submit with that amount | throws `ClaimRuleException` with Kind `Validation` |
| `Submit_rejects_future_incident_date` | IncidentDate 2026-11-27 | `Validation` |
| `Submit_rejects_blank_description` (Theory: "", "  ") | | `Validation` |
| `Assign_moves_to_under_review` | `Assign(officer1)` | Status `UnderReview`, AssignedOfficerId officer1, event `ClaimAssigned` |
| `Assign_twice_is_conflict` | `UnderReview().Assign(officer2)` | `Conflict` |
| `Request_info_moves_to_awaiting` | `UnderReview().RequestInformation(officer1,"Police report?")` | Status `AwaitingInformation`, one request with that question and Response null, event `InformationRequested` |
| `Request_info_by_other_officer_is_forbidden` | `RequestInformation(officer2, …)` | `Forbidden` |
| `Request_info_while_awaiting_is_conflict` | request twice | `Conflict` |
| `Request_info_blank_question_is_validation` | question `" "` | `Validation` |
| `Respond_returns_to_under_review` | request, then `RespondToInformationRequest(claimantA, req.Id, "Attached ref 123")` | Status `UnderReview`, Response set, RespondedAt = Now, event `InformationProvided` |
| `Respond_by_other_claimant_is_forbidden` | claimantB responds | `Forbidden` |
| `Respond_to_wrong_request_is_conflict` | random `requestId` | `Conflict` |
| `Respond_when_not_awaiting_is_conflict` | respond on an `UnderReview()` claim | `Conflict` |
| `Adjust_reserve_allowed_while_open_and_assigned` (two cases: UnderReview, AwaitingInformation) | `AdjustReserve(officer1, 7500m)` | Reserve `7500m`, event `ReserveAdjusted` |
| `Adjust_reserve_negative_is_validation` | `-1m` | `Validation` |
| `Adjust_reserve_after_settle_is_conflict` | settle, then adjust | `Conflict` |
| `Settle_closes_and_releases_reserve` | `UnderReview().Settle(officer1, 4200m)` | Status `Settled`, SettlementAmount `4200m`, Reserve `0m`, DecidedAt Now, IsOpen false, event `ClaimSettled` |
| `Settle_non_positive_is_validation` | `0m` | `Validation` |
| `Settle_while_awaiting_is_conflict` | | `Conflict` |
| `Settle_by_other_officer_is_forbidden` | | `Forbidden` |
| `Reject_closes_with_reason` | `Reject(officer1, "Not covered")` | Status `Rejected`, RejectionReason set, Reserve `0m`, event `ClaimRejected` |
| `Reject_blank_reason_is_validation` | | `Validation` |
| `Reject_rejected_claim_is_conflict` | reject twice | `Conflict` |
| `Each_action_appends_exactly_one_event` | Submit, Assign, Request, Respond, Adjust, Settle | `Events.Count` is 1, 2, 3, 4, 5, 6 after each step |

- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~ClaimTests"` → Expected: the build fails because `Claim` isn't defined.
- [ ] **Step 3: Implement the Domain files** to the Interfaces block above. Checks happen in this order: right person (Forbidden), then right state (Conflict), then valid input (Validation). The one exception is `Submit`, which only has Validation checks.
- [ ] **Step 4: Run** the same command → Expected: all tests pass.
- [ ] **Step 5: Commit** with `feat(claims): claim aggregate and state machine with unit tests`.

---

### Task 3: Claims persistence, seed data, users endpoint

**Files:**
- Create: `src/ClaimsApi/Data/{ClaimsDbContext,SeedData}.cs`, `Data/Migrations/*`, `Api/{CurrentUser,Dtos,UserEndpoints}.cs`
- Modify: `src/ClaimsApi/Program.cs`, `appsettings.json`

**Interfaces:**
- Consumes: Task 2 domain types.
- Produces:
  - `ClaimsDbContext` with `DbSet<User> Users`, `DbSet<Claim> Claims`, `DbSet<InformationRequest> InformationRequests`, `DbSet<ClaimEvent> ClaimEvents`
  - `SeedData.EnsureSeededAsync(ClaimsDbContext db, DateTimeOffset now)`
  - `SeedUsers` constants:

| Constant | GUID |
|---|---|
| `ClaimantAisha` | `00000000-0000-0000-0000-00000000c001` |
| `ClaimantDaniel` | `00000000-0000-0000-0000-00000000c002` |
| `OfficerMei` | `00000000-0000-0000-0000-00000000f001` |
| `OfficerArjun` | `00000000-0000-0000-0000-00000000f002` |
| `ManagerJames` | `00000000-0000-0000-0000-00000000a001` |

  - `CurrentUserFilter : IEndpointFilter` and `HttpContext.CurrentUser()` returning `User`
  - `RequireRole(this User u, params Role[] roles)`, which throws `ClaimRuleException(Forbidden)`
  - `UserDto(Guid Id, string Name, Role Role, Market? Market)`

- [ ] **Step 1: Write the `ClaimsDbContext` mapping**
  - **Columns:**
    - all enums stored as strings (`ConfigureConventions` → `Properties<TEnum>().HaveConversion<string>()`);
    - `Claim.Number` is an identity column (`UseIdentityAlwaysColumn`), with a unique index;
    - money columns are `numeric(18,2)`;
    - `Claim.Version` uses `IsRowVersion()`, which Npgsql maps to Postgres `xmin`;
    - `ClaimEvent.Payload` is `jsonb`.
  - **Relationships and indexes:**
    - `Claim` has `Events` and `InformationRequests` as cascade-owned one-to-many relationships;
    - index `ClaimEvents(PublishedAt, OccurredAt)`;
    - `DisplayNumber` and `IsOpen` are ignored (computed, not stored).
  - **Connection string:** `ConnectionStrings:Claims` = `Host=localhost;Port=5432;Database=claims;Username=postgres;Password=postgres`.
- [ ] **Step 2: Register services in `Program.cs`:** `AddDbContext`, `TimeProvider.System`, a JSON string-enum converter (`ConfigureHttpJsonOptions`), `AddProblemDetails`, and keep the template's `AddOpenApi`/`MapOpenApi` (serves `/openapi/v1.json` in Development). On startup: `await db.Database.MigrateAsync()` then `SeedData.EnsureSeededAsync`.
- [ ] **Step 3: Write `SeedData`:** the 5 users above (Aisha is MY, Daniel is SG), plus 6 claims created **through the domain methods** so each has real events:
  - 2 Submitted;
  - 1 UnderReview with Mei;
  - 1 AwaitingInformation with Arjun;
  - 1 Settled;
  - 1 Rejected.

  The claims are spread across MY and SG. Seeding runs only when `Users` is empty.
- [ ] **Step 4: Write `CurrentUserFilter` and `GET /api/users`.** The filter returns **401** `ProblemDetails` when the header is missing, isn't a GUID, or names an unknown user. `/api/users` is the one route outside the filter, because the role switcher needs it before anyone is selected.
- [ ] **Step 5: Create the migration:** `dotnet ef migrations add Initial --project src/ClaimsApi --output-dir Data/Migrations` → Expected: `Done.`
- [ ] **Step 6: Verify.** Run `dotnet run --project src/ClaimsApi`, then `curl -s localhost:5100/api/users` → Expected: 5 users, with `"role":"Officer"` written as a string. Then `docker compose exec postgres psql -U postgres -d claims -c 'select count(*) from "Claims"'` → Expected: `6`.
- [ ] **Step 7: Commit** with `feat(claims): EF Core persistence, migration, seed data, users endpoint`.

---

### Task 4: Claim endpoints, error mapping, smoke tests

**Files:**
- Create: `src/ClaimsApi/Api/{ClaimEndpoints,ErrorHandler}.cs`, `tests/ClaimsPlatform.Tests/Smoke/LifecycleSmokeTests.cs`
- Modify: `Api/Dtos.cs`, `Program.cs`

**Interfaces:**
- Consumes: Tasks 2–3.
- Produces (records in `Dtos.cs`, also mirrored in Angular `models.ts`):

```csharp
record SubmitClaimRequest(ClaimType Type, DateOnly IncidentDate, string Description, decimal ClaimedAmount);
record RequestInformationRequest(string Question);
record RespondRequest(string Response);
record AdjustReserveRequest(decimal Reserve);
record SettleRequest(decimal Amount);
record RejectRequest(string Reason);
record ClaimSummary(Guid Id, string Number, ClaimStatus Status, ClaimType Type, Market Market, string Currency,
  decimal ClaimedAmount, decimal Reserve, Guid ClaimantId, string ClaimantName, Guid? AssignedOfficerId,
  string? AssignedOfficerName, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
record InformationRequestDto(Guid Id, string Question, string? Response, DateTimeOffset RequestedAt, DateTimeOffset? RespondedAt);
record TimelineEntry(ClaimEventType Type, Guid ActorId, string ActorName, DateTimeOffset OccurredAt);
record ClaimDetail(ClaimSummary Summary, DateOnly IncidentDate, string Description, decimal? SettlementAmount,
  string? RejectionReason, DateTimeOffset? DecidedAt, IReadOnlyList<InformationRequestDto> InformationRequests,
  IReadOnlyList<TimelineEntry> Timeline);
```

**Routes:** as in §7. Status codes:
- `POST /api/claims` returns **201** with `ClaimDetail` and a `Location` header.
- Every action route returns **200** with the updated `ClaimDetail`.
- An unknown claim ID returns **404**.

**Who sees what on `GET /api/claims`:**
- Claimant: their own claims.
- Officer: claims that are `Submitted` with no officer, plus claims assigned to them.
- Manager: all claims.
- All roles: optional `?status=`, newest first.

`GET /api/claims/{id}` is allowed for the claim's owner and for any officer or manager. Any other claimant gets **403**.

**`ErrorHandler : IExceptionHandler`** maps exceptions to `ProblemDetails`:

| Exception | Status |
|---|---|
| `ClaimRuleException` Validation | 400 |
| `ClaimRuleException` Forbidden | 403 |
| `ClaimRuleException` Conflict | 409 |
| `DbUpdateConcurrencyException` | 409, title "Claim was changed by someone else" |
| `BadHttpRequestException` (malformed body or unknown enum value) | 400 |

- [ ] **Step 1: Write the failing smoke tests** in `LifecycleSmokeTests`, marked `[Trait("Category","Smoke")]`. They use an `HttpClient` with base address `http://localhost:5100` and a helper `As(Guid userId)` that sets `X-User-Id`.

| Test | Assert |
|---|---|
| `Full_lifecycle_settle` | Aisha submits (201) → Mei assigns (200, UnderReview) → Mei requests info (AwaitingInformation) → Aisha responds (UnderReview) → Mei adjusts reserve to 6000 → Mei settles 5500 (Settled, Reserve 0). The timeline has 6 entries, in order. |
| `Reject_path` | submit → assign → reject "Not covered" → Rejected |
| `Concurrent_assign_has_one_winner` | Aisha submits. Mei and Arjun `POST /assign` simultaneously (`Task.WhenAll`). The status codes are exactly `{200, 409}`. |
| `Unknown_user_is_401` | No header gives 401. Header `Guid.NewGuid()` gives 401. |
| `Other_claimant_is_403` | Daniel gets Aisha's claim: 403 |
| `Officer_cannot_submit` | Mei calls `POST /api/claims`: 403 |
| `Settle_unassigned_claim_is_409` | Mei settles a freshly submitted claim: 409 |
| `Unknown_enum_is_400` | Body `{"type":"Boat",...}`: 400 with a `application/problem+json` content type |

- [ ] **Step 2: Run** `dotnet test --filter Category=Smoke` with the API running → Expected: FAIL with 404s, because the routes don't exist yet.
- [ ] **Step 3: Implement `ClaimEndpoints.MapClaimEndpoints(this IEndpointRouteBuilder app)`** as a `MapGroup("/api/claims").AddEndpointFilter<CurrentUserFilter>()`. Each action handler:
  1. calls `RequireRole`;
  2. loads the claim with `Include(InformationRequests)`;
  3. calls the domain method with `timeProvider.GetUtcNow()`;
  4. calls `SaveChangesAsync`;
  5. returns `ClaimDetail` (looking up user names for the summary and timeline in a single `Users` query).

  Register `ErrorHandler` with `AddExceptionHandler<ErrorHandler>()` and `app.UseExceptionHandler()`.
- [ ] **Step 4: Run** `dotnet test --filter Category=Smoke` → Expected: all 8 pass. Run `dotnet test --filter "Category!=Smoke"` → Expected: Task 2's tests still pass.
- [ ] **Step 5: Commit** with `feat(claims): REST endpoints, ProblemDetails mapping, smoke tests`.

---

### Task 5: Outbox publisher to Kafka

**Files:**
- Create: `src/ClaimsApi/Messaging/OutboxPublisher.cs`
- Modify: `Program.cs`, `appsettings.json` (`Kafka:BootstrapServers` = `localhost:9092`, `Kafka:Topic` = `claim-events`)

**Interfaces:**
- Consumes: `ClaimsDbContext.ClaimEvents` and `Claims`.
- Produces: Kafka messages with key = `ClaimId.ToString()` and this value (camelCase JSON, enums as strings):

```json
{ "eventId", "type", "claimId", "claimNumber", "claimantId", "occurredAt", "data" }
```

  `data` is the stored `Payload`, embedded as raw JSON.

- [ ] **Step 1: Implement `OutboxPublisher : BackgroundService`.**
  - **On start:** create the topic with `AdminClient.CreateTopicsAsync` (3 partitions, replication factor 1). If it fails with `TopicAlreadyExists`, ignore it. If Kafka is unreachable, retry every 5 seconds without crashing the host.
  - **Every 1 second:**
    1. Create a scope.
    2. Read up to 100 rows where `PublishedAt == null`, ordered by `OccurredAt`, joined to `Claims` for `Number` and `ClaimantId`.
    3. For each row in order, `await ProduceAsync` the message. If that succeeds, set `PublishedAt`. On the first failure, stop the batch and wait for the next tick.
    4. Call `SaveChangesAsync` after the loop.
  - **Producer config:** `EnableIdempotence = true`, `Acks = Acks.All`.
- [ ] **Step 2: Verify publishing.** Run the API, then:

  `docker compose exec kafka /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server localhost:9092 --topic claim-events --from-beginning --property print.key=true --timeout-ms 10000`

  Expected: one message for each seeded claim event, with the claim GUID as the key and `"type":"ClaimSubmitted"` in the first message.
- [ ] **Step 3: Verify the outbox is drained:** `select count(*) from "ClaimEvents" where "PublishedAt" is null` → Expected: `0`.
- [ ] **Step 4: Verify that Kafka being down doesn't block claims (Review Focus 2).**
  1. `docker compose stop kafka`.
  2. Submit a claim with curl as Aisha → Expected: **201**.
  3. Rerun the query from Step 3 → Expected: at least `1`.
  4. `docker compose start kafka`, wait about 15 seconds, and rerun the query → Expected: `0`.
- [ ] **Step 5: Commit** with `feat(claims): transactional outbox publisher to Kafka`.

---

### Task 6: Notifications service

**Files:**
- Create: everything under `src/NotificationsService/` except the template files; `tests/ClaimsPlatform.Tests/Notifications/NotificationTextTests.cs`

**Interfaces:**
- Consumes: the Kafka message shape from Task 5.
- Produces:
  - `record ClaimEventMessage(Guid EventId, string Type, Guid ClaimId, string ClaimNumber, Guid ClaimantId, DateTimeOffset OccurredAt, JsonElement Data)`
  - `static string? NotificationText.For(ClaimEventMessage m)`
  - Entities `Notification { Guid Id; Guid RecipientId; Guid ClaimId; string ClaimNumber; string Message; DateTimeOffset CreatedAt; }` and `ProcessedEvent { Guid EventId (primary key); DateTimeOffset ProcessedAt; }`
  - `GET /api/notifications` returns `NotificationDto(Guid Id, Guid ClaimId, string ClaimNumber, string Message, DateTimeOffset CreatedAt)[]` for the user in `X-User-Id`, newest first. A missing or invalid header gets **401**.

**Notification text (exact):**

| Event type | Message |
|---|---|
| ClaimSubmitted | `We've received your claim {n}.` |
| ClaimAssigned | `Your claim {n} is now being reviewed by a claims officer.` |
| InformationRequested | `We need more information for claim {n}: {question}` |
| ClaimSettled | `Your claim {n} has been settled for {currency} {amount:N2}.` |
| ClaimRejected | `Your claim {n} was not approved: {reason}` |
| anything else | `null` (no notification, but the event is still marked processed) |

- [ ] **Step 1: Write the failing `NotificationTextTests`.** One test per row in the table, asserting the exact string for `n = "CLM-000007"`. Settled uses `currency "MYR"` and `amount 5500` → `"Your claim CLM-000007 has been settled for MYR 5,500.00."` Format with `CultureInfo.InvariantCulture`. Add a test that `ReserveAdjusted` gives `null`.
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~NotificationTextTests"` → Expected: fails to compile.
- [ ] **Step 3: Implement `NotificationText.For`.** Run the tests → Expected: they pass.
- [ ] **Step 4: Implement the database, migration and endpoint.**
  - `NotificationsDbContext` with connection string `Database=notifications` (otherwise the same as Task 3).
  - Migration: `dotnet ef migrations add Initial --project src/NotificationsService`.
  - `MigrateAsync` on startup, then the `/api/notifications` endpoint.
- [ ] **Step 5: Implement `ClaimEventsConsumer : BackgroundService`.**
  - **Threading:** the consume loop runs inside `Task.Run`, because `Consume` is a blocking call.
  - **Consumer config:** `GroupId = "notifications"`, `AutoOffsetReset = Earliest`, `EnableAutoCommit = false`.
  - **For each message:**
    1. Deserialise it.
    2. In one new scope, if `ProcessedEvents` already contains the `EventId`, skip to step 4.
    3. Otherwise add a `ProcessedEvent`, plus a `Notification` if `For(m)` isn't null, and call `SaveChangesAsync`. Both rows are written in the same transaction.
    4. `consumer.Commit(result)`.
  - **Error handling:** a unique-key violation on `ProcessedEvent` counts as "already processed". A message that can't be deserialised is logged and committed, so one bad message doesn't block the partition.
  - **Logging:** log `"Email sent to {RecipientId}: {Message}"` at Information level for each notification created.
- [ ] **Step 6: Verify end to end and that duplicates are ignored (Review Focus 3).**
  1. Run both services.
  2. `curl -s -H "X-User-Id: 00000000-0000-0000-0000-00000000c001" localhost:5200/api/notifications` → Expected: Aisha's seed notifications. Note the count.
  3. Stop the Notifications service.
  4. `docker compose exec kafka /opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server localhost:9092 --group notifications --topic claim-events --reset-offsets --to-earliest --execute`
  5. Restart the service, wait 10 seconds, and repeat step 2 → Expected: **the same count** (every event was redelivered, and no duplicate notifications were created).
- [ ] **Step 7: Commit** with `feat(notifications): idempotent Kafka consumer and notifications endpoint`.

---

### Task 7: Dashboard endpoint

**Files:**
- Create: `src/ClaimsApi/Api/DashboardEndpoints.cs`
- Modify: `Dtos.cs`, `Program.cs`, `LifecycleSmokeTests.cs`

**Interfaces:**
- Produces:

```csharp
record StatusCount(ClaimStatus Status, int Count);
record OfficerWorkload(Guid OfficerId, string Name, int Open, int ClosedLast30Days, double? AvgDaysToDecision);
record CurrencyExposure(string Currency, decimal TotalReserve, int OpenClaims);
record DashboardDto(IReadOnlyList<StatusCount> OpenByStatus, int Unassigned, IReadOnlyList<OfficerWorkload> Officers,
  IReadOnlyList<CurrencyExposure> Exposure, int OpenOver7Days, int OpenOver30Days);
```

  `GET /api/dashboard` is available to Officer and Manager; a Claimant gets **403**.

- [ ] **Step 1: Write the failing smoke test `Dashboard_exposure_tracks_reserve`.**
  1. Read the `MYR` exposure as Mei.
  2. Aisha submits a 1,000 claim; MYR exposure goes up by exactly `1000m` and `OpenClaims` by 1.
  3. Mei assigns and settles it; MYR exposure returns to its starting value.

  Also add `Claimant_dashboard_is_403`.
- [ ] **Step 2: Run** the smoke tests → Expected: those two fail with 404.
- [ ] **Step 3: Implement the queries.**
  - "Open" means `Status` is `Submitted`, `UnderReview` or `AwaitingInformation`.
  - **Exposure:** `GroupBy(Currency)` with `Sum(Reserve)` and `Count`, run in Postgres.
  - **Officer workload:** every user with role `Officer`, even with zero claims.
  - **`ClosedLast30Days` and `AvgDaysToDecision`:** load `(AssignedOfficerId, CreatedAt, DecidedAt)` for claims decided in the last 30 days and calculate in memory. The data is small, and this avoids depending on how Npgsql translates `TimeSpan` arithmetic.
  - **Ageing:** count by `CreatedAt` against now minus 7 days and now minus 30 days.
- [ ] **Step 4: Run** `dotnet test --filter Category=Smoke` → Expected: all pass.
- [ ] **Step 5: Commit** with `feat(claims): dashboard with workload and per-currency exposure`.

---

### Task 8: Angular shell and claimant pages

**Files:**
- Create: `web/` via the CLI; `web/proxy.conf.json`
- Create in `web/src/app/`:
  - `core/models.ts`
  - `core/current-user.service.ts`
  - `core/user-id.interceptor.ts`
  - `core/claims-api.service.ts`
  - `core/notifications-api.service.ts`
  - `claimant/my-claims.ts`, `claimant/new-claim.ts`, `claimant/claim-detail.ts`
- Modify: `app.config.ts`, `app.routes.ts`, `app.ts`, `app.html`, `app.css`, `angular.json` (`serve.options.proxyConfig`)

**Interfaces:**
- Consumes: the DTOs from Tasks 3, 4 and 6, mirrored one-to-one as TypeScript interfaces in `models.ts`, with enums as string-literal unions.
- Produces:
  - `CurrentUserService`: `user = signal<UserDto | null>`, `select(u: UserDto)`, which persists the ID to `localStorage` under key `claimdesk.userId`.
  - `ClaimsApiService` with one method per route in §7, each returning an `Observable`.

- [ ] **Step 1: Scaffold.** Run `ng new web --style=css --ssr=false --skip-git --routing` and pick "none" for the AI-config prompt.
  - **Proxy config.** The order matters: the more specific entry must come first.

```json
{ "/api/notifications": { "target": "http://localhost:5200" }, "/api": { "target": "http://localhost:5100" } }
```

- [ ] **Step 2: Write the core services.**
  - The interceptor adds `X-User-Id` when a user is selected.
  - **Header:** product name "ClaimDesk", plus a `<select>` of users from `/api/users` grouped by role. Selecting a user navigates to that role's home: Claimant → `/claims`, Officer → `/work`, Manager → `/dashboard`. Until the stretch dashboard page exists, `/dashboard` is a placeholder that says "Dashboard: see `GET /api/dashboard`".
  - **Palette:** a neutral slate and teal of your own choosing, with no third-party branding.
- [ ] **Step 3: Write the claimant pages.**
  - `/claims`: a table of number, type, status, claimed amount with currency, and last updated. A "Report an incident" button goes to `/claims/new`.
  - `/claims/new`: a reactive form for type, incident date, description and claimed amount. On submit, call the API and go to the new claim's detail page. Server errors are shown from `ProblemDetails.detail`.
  - `/claims/:id`: claim fields, the decision (if any), and the timeline. If the claim is `AwaitingInformation`, show the open question and a response form.
  - Lists refresh after each action, and `/claims` polls every 10 seconds.
- [ ] **Step 4: Verify.** `cd web && npx ng build` → Expected: builds without errors. Then, with both APIs and `npm start` running, check manually:
  1. Select Aisha, submit a claim, and confirm it appears in `/claims` as Submitted.
  2. Open it and confirm the timeline shows ClaimSubmitted.
  3. Submit with amount 0 and confirm the server's 400 message is shown on the form.
- [ ] **Step 5: Commit** with `feat(web): Angular shell, role switcher, claimant pages`.

---

### Task 9: Officer pages

**Files:**
- Create: `web/src/app/officer/work-queue.ts`, `web/src/app/officer/work-claim.ts`
- Modify: `app.routes.ts`

- [ ] **Step 1: `/work`:** two tables, "Unassigned" (with a Pick up button) and "My claims". On a 409 from pick-up, show "Someone else picked this up" and refresh.
- [ ] **Step 2: `/work/:id`:** claim detail with forms for request info, adjust reserve, settle (amount) and reject (reason). Buttons are only enabled in the states where the matching domain method is allowed. Show `ProblemDetails.detail` on error.
- [ ] **Step 3: Verify manually.** Run through the full lifecycle in the browser: Aisha submits → Mei picks up → Mei requests info → Aisha answers → Mei settles → Aisha sees Settled.
- [ ] **Step 4: Commit** with `feat(web): officer work queue and claim actions`.

---

### Stretch (hours 4–5, in this order; each is its own commit)

1. **Dashboard page** `/dashboard`: tables for `DashboardDto`, with exposure per currency and amounts shown with currency codes.
2. **Notifications badge** in the header: count from `/api/notifications`, polled every 10 seconds, with a dropdown list.
3. **Containerised services:** a Dockerfile per service, services added to compose. Requires setting `KAFKA_ADVERTISED_LISTENERS` for both the host and the Docker network.
4. **API integration tests** with `WebApplicationFactory` against the compose Postgres.
5. **Server-sent events** for claim updates, replacing polling.
6. **Generated TypeScript client** from `/openapi/v1.json`.

### Docs (folded into the last task you complete)

- **README:** how to run, the seeded user IDs, how to run the unit and smoke tests, and the known limitations (taken from the design's non-goals).
- **`docs/AI_JOURNAL.md`:** kept current as you go.
