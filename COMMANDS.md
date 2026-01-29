# Mineral Kingdom – Commands

This document lists common commands used to develop, test, and run the
Mineral Kingdom backend API.

All commands assume you are at the **repository root**, unless stated otherwise.

---

## Prerequisites

- .NET SDK 8.x
- Docker (required for integration tests)
- PostgreSQL (optional for local runtime)

---

## Navigate to the Solution

```
cd mineral-kingdom-api/MineralKingdom
```

---

## Restore Dependencies

```
dotnet restore MineralKingdom.sln
```

---

## Build the Solution

```
dotnet build MineralKingdom.sln
```

---

## Run the API

```
dotnet run --project MineralKingdom.Api
```

---

## Run All Tests

```
dotnet test MineralKingdom.sln
```

---

## Run Unit Tests Only

```
dotnet test MineralKingdom.Domain.Tests
```

---

## Run Integration Tests Only

```
dotnet test MineralKingdom.Api.IntegrationTests
```

Integration tests automatically start and stop a Postgres container using
Testcontainers. Docker must be running.

---

## Docker (Optional Local Postgres)

```
docker run --name mk-postgres \
  -e POSTGRES_USER=mk \
  -e POSTGRES_PASSWORD=mk_dev_pw \
  -e POSTGRES_DB=mk \
  -p 5432:5432 \
  postgres:16
```

Stop and remove the container:

```
docker stop mk-postgres
docker rm mk-postgres
```

---

## Environment Variables (Optional)

```
MK_DB__HOST=localhost
MK_DB__PORT=5432
MK_DB__NAME=mk
MK_DB__USER=mk
MK_DB__PASSWORD=mk_dev_pw
```

---

## GitHub Actions CI

CI runs automatically:
- On every pull request to `main`
- On every push to `main`

CI performs:
- Restore
- Build
- Unit tests
- Integration tests (with Postgres via Testcontainers)

---

## Notes

- Do not commit `.env` files
- Docker is only required for integration tests
- CI is the source of truth for correctness
