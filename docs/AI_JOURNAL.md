# AI Working Journal

A running log of how I used AI on this assessment: what I asked, what I accepted, what I challenged, and what I overrode. Entries are in time order and not polished.

- **Tool:** Claude Code (Claude Opus 5.5), run in the terminal against this repository.
- **Times:** local time, 25 Nov 2026.

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
- **Why:** _(fill in)_

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
