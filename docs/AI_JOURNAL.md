# AI Working Journal

A running log of how I used AI on this assessment: what I asked, what I accepted, what I challenged, and what I overrode. Entries are in time order and not polished.

- **Tool:** Claude Code (Claude Opus 5.5), run in the terminal against this repository.
- **Times:** local time, 25–26 Nov 2026 (the sprint ran from 23:38 to 01:35).

---

## 23:38: Reading the brief

- **Asked:** Read the assessment brief (.docx) and summarise it.
- **AI did:** Pulled the text out of the .docx and summarised the scope, deliverables and constraints. It flagged two rules that are easy to miss: no public hosting, and no Chubb branding (the only image in the brief is the logo). It also checked which toolchains are on my machine: .NET SDK 9, Java 23 without Maven or Gradle, Node 22, Angular CLI 21, Docker.
- **Decision:** Accepted. The brief stays out of the repo (`.gitignore`) because it carries third-party branding, and the app gets a neutral name and palette.

## 23:42: C# or Java?

- **Asked:** Which is the faster language, C# or Java?
- **AI said:** At runtime the difference doesn't matter for this app, because database and Kafka round-trips dominate. Two things do differ:
  - Build speed: .NET minimal APIs plus EF Core need less boilerplate than Spring.
  - Kafka: Spring Kafka (`@KafkaListener`) is more convenient than .NET's Confluent.Kafka, where I write the consumer loop myself.
  - The deciding factor should be which one I can defend line by line.
- **Decision:** Accepted. I went with **.NET**.
- **Why:** I'm more confident defending C#. The cost is owning a hand-written Kafka consumer loop, which is about 40 lines and something I can explain.

## 23:43: Challenged the .NET version

- **Asked:** Should we upgrade to .NET 10 first?
- **What happened:** The AI had implicitly planned on **.NET 9**, simply because that SDK was installed. When I questioned it, the AI checked Microsoft's lifecycle pages and found that .NET 9 (and .NET 8) **reached end of support on 10 Nov 2026**, two weeks ago.
- **AI recommended:** .NET 10, the LTS release supported until Nov 2028, rather than .NET 11, an STS release that shipped 10 Nov 2026. Its reasons:
  - Library support for .NET 11 (Kafka client, EF Core Postgres provider) may still be catching up.
  - The AI's training data covers .NET 10 well and .NET 11 only from previews, so it would generate .NET 10 code more reliably.
- **Decision:** **Challenged, then corrected.** I installed SDK 10.0.404 and pinned it with `global.json`.
- **Lesson:** The AI defaulted to "whatever is installed" without checking whether it was still supported. I need to question version and dependency choices rather than accept them silently.

## 23:49: Told the AI I'm new to Kafka, Angular and EF Core

- **Context:** This is my first time using any of the three. C# is the language I'm most confident in.
- **AI proposed:** Keep each technology down to as few concepts as possible.
  - Kafka: one topic, with REST for anything a user waits on.
  - EF Core: one context and plain LINQ queries.
  - Angular: a thin front end, built last.
  - I only commit code I can explain back.
- **Correction:** The AI had written my reason for choosing .NET into this journal before I'd given one. It pointed this out itself, and I confirmed the reason was accurate.
- **Decision:** Accepted.

## 23:54: EF Core rather than Dapper

- **Asked:** Is EF Core mandatory, and what are the alternatives?
- **AI said:** It isn't mandatory. Dapper (plain SQL) or raw Npgsql would also work. The AI leaned towards **Dapper**, on the condition that I'm comfortable with SQL, because every database call would be visible SQL and there'd be less hidden behaviour to explain.
- **Decision:** **Overrode the AI's lean: went with EF Core.**
- **Why:** EF Core fits ClaimDesk because the work is managing claim changes reliably with little hand-written database code. For example, its row-version token (Postgres xmin) stops two officers picking up the same claim, and migrations create the schema. Dapper could support the same design, but I'd write that persistence logic myself.

## 23:56: Service boundaries

- **AI offered three options:**
  - A: one service, with Kafka running inside it.
  - B: a Claims API plus a small Notifications service.
  - C: one service per business step.
- **AI recommended B.** Anything a user waits on goes over REST. Side effects that must never slow down or break a claim decision go through Kafka. A gives no real reason to use Kafka. C would need consistency across four databases for what is really one claim.
- **Decision:** Accepted B.
- **Next:** the AI wrote `docs/DESIGN.md` for me to review before any code is written.

## 00:32: Approved the design

- **Decision:** Approved `docs/DESIGN.md` as written.
- **Commits:** the AI commits at each step of the build order, so the history shows how the work progressed.

## 00:53–01:35: Build (AI implemented the plan task by task; I watched and approved "native" execution)

- **Process:** test-first throughout. 61 xUnit tests (claim rules, notification text, and smoke tests against the running services). A commit after each task, on branch `build/claims-platform`.
- **Bugs the AI caught by verifying, not by trusting the code:**
  - The Postgres row-version mapping required `ValueGeneratedNever` on Guid keys; without it EF would have issued UPDATEs for new rows.
  - The dashboard sorted on a constructed record, which EF can't translate (500 error). Fixed.
  - **Angular circular dependency (NG0200):** the user switcher was empty. Found in the browser, not by the build.
- **Automated security reviews flagged several issues during the build. Fixed (each test-first):**
  - Services now refuse to start outside Development (fake-login header).
  - Malformed requests no longer echo internal type names.
  - A malformed Kafka event could stall the consumer forever; it is now skipped.
  - Claim text removed from logs.
  - **The reserve was being sent to claimants.** The AI noticed this while building the claimant page, and an automated security review flagged it too. The reserve is the insurer's internal estimate of what a claim will cost, so claimants shouldn't see it. It is now hidden on the server, not just in the UI.
- **Not changed (decisions in the design I approved, now in the README's known limitations):** any officer can view any claim; no per-market scoping.
- **Verified by hand:**
  - Stopping Kafka doesn't block claims; the outbox drains when Kafka comes back.
  - Replaying the whole topic creates no duplicate notifications.
  - Full lifecycle in the browser across claimant, officer and manager.
- **Asked mid-build (01:17): xUnit, NUnit or MSTest?** The AI said xUnit, the `dotnet new xunit` default and the most widely used in modern .NET. Unit tests need no infrastructure; smoke tests are tagged `Category=Smoke` and call the running services. Accepted.
- **Mistake to own:** commit `653a15d` (a notifications fix) also swept in the untouched Angular CLI scaffold, because it was made right after `ng new` with `git add -A`.
- **Why:** I didn't override anything during the build; I reviewed each step and accepted it.

---

## 01:35: Where it stands at submission

**Time:** about 2 hours (23:38 to 01:35), inside the 2–3 hour target and well under the 5-hour cap. About 1 hour went on reading the brief, deciding the stack and approving the design and plan; about 40 minutes went on the build.

### What was built

- **Claims API (.NET 10, EF Core, Postgres).** The full lifecycle over REST:
  - **Claimant:** reports an incident, tracks the claim, answers information requests, sees the decision.
  - **Officer:** picks up a claim, requests information, adjusts the reserve, settles or rejects.
  - **Rules** live in the `Claim` aggregate; rule violations come back as 400/403/409 ProblemDetails.
  - **Two officers picking up the same claim:** one gets 200, the other 409, enforced by the Postgres row version.
- **Kafka with one defensible job.** Claim events are written to an outbox in the same transaction as the change, then published to one topic. A separate Notifications service consumes them idempotently. Claim decisions never wait on Kafka.
- **Manager and officer dashboard.** Outstanding claims, team workload and liability exposure (the sum of open reserves), per currency, never converted.
- **Angular front end** with a role switcher and pages for all three roles: my claims, report an incident and claim detail; work queue and work claim; dashboard.
- **Tests:** 61 xUnit tests, 45 unit tests on claim rules and notification text, 16 smoke tests over HTTP against the running services.
- **Verified by hand:** concurrent pick-up, Kafka down while claims are created, a full topic replay with no duplicates, the full lifecycle in the browser.

### What I left out, and why

Most of these were cut up front in the design I approved (DESIGN.md §2 and §13), so the time went on the claim lifecycle and the Kafka path rather than on breadth.

| Left out | Why |
|---|---|
| Real authentication | A role switcher sending `X-User-Id` shows all three roles without building OIDC. Both services refuse to start outside Development so the shim can't be deployed by accident. |
| Policies, coverage checks, payments | These belong to other systems (policy admin, payments). "Settled" means a settlement was decided. |
| Currency conversion | Exposure is reported per currency; summing SGD and IDR without a rate source would be wrong. |
| File uploads, fraud scoring, real email or SMS, localisation | Breadth that doesn't change the architecture. "Email sent" is a log line in the Notifications service. |
| Reassigning or withdrawing claims, per-market scoping | Extra state transitions and permissions; one team across markets is enough to show the workflow. |
| Notifications badge in the UI | Stretch item, not reached. Notifications are stored and served by the Notifications service API, but the UI doesn't show them yet. |
| Containerised services, generated TypeScript client, server-sent events | Stretch items, not reached. The services run with `dotnet run`; the UI polls every 10 seconds. |
| Dead-letter topic | The consumer retries transient failures without a cap so no event is dropped during an outage; malformed events are skipped and logged. |

### How I directed the AI

- **Challenged:** the .NET version. The AI planned on .NET 9 because it was installed; it had reached end of support, so we moved to .NET 10 LTS.
- **Overrode:** the AI's lean towards Dapper; I chose EF Core.
- **Corrected:** the AI writing my reasons into this journal before I'd given them.
- **Accepted, after review:** the C#/.NET recommendation, service split B, the design and plan, and each build step.
- **Caught by verification rather than trust:** the Guid key mapping, the untranslatable dashboard query, and the Angular circular dependency, which only showed up in the browser.
