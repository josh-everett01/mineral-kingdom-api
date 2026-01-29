# Mineral Kingdom API

 This is the backend API for Mineral Kingdom, a full-stack application that supports
buying, selling, and auctioning mineral and crystal specimens.

This repository contains the **backend service**, built with **.NET 8**,
**PostgreSQL**, and a test-first, CI-enforced architecture.

---

## Tech Stack

- .NET 8
- ASP.NET Core Web API
- Entity Framework Core
- PostgreSQL
- xUnit (unit + integration tests)
- Testcontainers (ephemeral Postgres for integration tests)
- GitHub Actions (CI)

---

## Repository Structure

```
mineral-kingdom-api/
├─ .github/
│  └─ workflows/
│     └─ ci.yml
├─ mineral-kingdom-api/
│  └─ MineralKingdom/
│     ├─ MineralKingdom.sln
│     ├─ MineralKingdom.Api/
│     ├─ MineralKingdom.Contracts/
│     ├─ MineralKingdom.Domain/
│     ├─ MineralKingdom.Infrastructure/
│     ├─ MineralKingdom.Worker/
│     ├─ MineralKingdom.Domain.Tests/
│     └─ MineralKingdom.Api.IntegrationTests/
├─ TESTING.md
├─ COMMANDS.md
├─ global.json
└─ README.md
```

---

## Architecture Overview

The application follows a layered architecture:

- **API**
  - HTTP endpoints and controllers
  - Request/response handling

- **Domain**
  - Core business logic and domain models
  - No infrastructure or database dependencies

- **Infrastructure**
  - Database access
  - EF Core persistence

This separation keeps business logic testable and the system maintainable as
features grow.

---

## Testing & CI

- Unit tests validate domain logic
- Integration tests run against a real Postgres instance using Testcontainers
- GitHub Actions runs build + tests on every PR and push to `main`

See:
- TESTING.md for testing details
- COMMANDS.md for common development commands


Detailed documentation will be added incrementally as features are implemented.
