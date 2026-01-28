# Testing – Mineral Kingdom

## Test Types

### Unit Tests

- **Project:** `MineralKingdom.Domain.Tests`
- **Framework:** xUnit
- **Purpose:**
  - Domain rules
  - Business logic
  - No database or infrastructure dependencies

Run:
```bash
dotnet test MineralKingdom.Domain.Tests
```

## Integration Tests

**Project**: MineralKingdom.Api.IntegrationTests

**Framework**: xUnit

**Database**: Postgres via Testcontainers

**Purpose**:

- Validate real API + EF Core + Postgres behavior

- Apply migrations automatically

- No dependency on local docker-compose or shared databases

### What is tested:

- API bootstrapping

- EF Core migrations

- Real write/read via /db-ping

### Run:

```bash
dotnet test MineralKingdom.Api.IntegrationTests
```

### Notes

- Integration tests spin up ephemeral Postgres containers and clean up automatically.

- Tests are deterministic and CI-safe.

- No secrets are required for test execution.