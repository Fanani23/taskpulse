# C# on Linux — REST API + WebSocket server

Two small ASP.NET Core (.NET 10 LTS) services, built and deployed on Ubuntu (WSL 2), with the
production plumbing around them: systemd units with hardening, nginx reverse proxy with WebSocket
upgrade, Docker images, integration tests, and a smoke test.

| Service | What it does | Port |
|---|---|---|
| `TaskPulse.Api` | CRUD for *tasks* (+ stats, search), a *catalog* of reference data, per-user *preferences* and file *uploads* on **PostgreSQL via EF Core** (migrations, optimistic concurrency, data survives restarts); paging + filtering, validation, OpenAPI, health checks | 5080 |
| `TaskPulse.Realtime` | WebSocket echo / broadcast / ping with a connection registry, graceful shutdown, browser test client | 5090 |
| nginx | Single public entry point in front of both, handles the WebSocket `Upgrade` | 8088 |
| PostgreSQL 18 | The store. Service connects over the Unix socket with peer auth — no password anywhere | 5433 |

```
taskpulse/
├── src/
│   ├── TaskPulse.Api/          Program.cs, Controllers/ → Services/ → Repositories/ → Data/ (EF Core), Models/ (DTOs), Infrastructure/
│   └── TaskPulse.Realtime/         Program.cs, Controllers/ (/ws, /stats), Services/ (session, connection manager, router), Models/, wwwroot/
├── tests/
│   ├── TaskPulse.Api.Tests/    26 integration tests through TestServer, each class on its own throw-away PostgreSQL database
│   └── TaskPulse.Realtime.Tests/   5 integration tests through TestServer's WebSocket client
├── deploy/
│   ├── systemd/                     taskpulse-api.service, taskpulse-realtime.service
│   ├── nginx/                       taskpulse.conf (host), taskpulse.compose.conf (docker)
│   └── docker/                      multi-stage Dockerfiles
├── scripts/
│   ├── install.sh                   provision PostgreSQL roles/DBs, publish → /opt/taskpulse, units, nginx  (sudo)
│   ├── test.sh                      dotnet test against the local cluster (detects its port)
│   ├── smoke-test.sh                end-to-end check of a running deployment
│   ├── seed.sh                      40 sample tasks + the catalog kinds, written through the API (idempotent; --force to add again)
│   └── run-dev.sh                   both services from source with hot reload
├── docker-compose.yml
├── Directory.Build.props            net10.0, nullable, warnings-as-errors, invariant globalization
└── global.json                      SDK 10.0.1xx
```

## Quick start

Prerequisites: Ubuntu 24.04+/WSL 2, `sudo apt install dotnet-sdk-10.0 postgresql nginx`.

### 1. Build and test

```bash
dotnet build TaskPulse.sln -c Release     # 0 warnings — warnings are errors
sudo scripts/install.sh                    # once: creates the taskpulse_dev role the tests use (and deploys)
scripts/test.sh                            # 31 tests on the real PostgreSQL cluster
```

### 2. Run — pick one

**From source (development)**

```bash
scripts/run-dev.sh                          # hot reload; REST on :5080, WebSocket on :5090
```

**As systemd services behind nginx (what the demo uses)**

```bash
sudo scripts/install.sh                     # idempotent; re-run to redeploy
scripts/smoke-test.sh                       # exercises everything through nginx on :8088
sudo scripts/install.sh --uninstall         # clean removal
```

**Docker**

```bash
docker compose up --build                   # REST :5080, WS :5090, nginx :8088
```

### 3. Try it

```bash
# REST
curl -s http://127.0.0.1:8088/api/tasks | jq
curl -s -X POST http://127.0.0.1:8088/api/tasks -H 'Content-Type: application/json' \
     -d '{"title":"Review the C# assignment","description":"Sat 19 Sep"}' | jq
curl -s http://127.0.0.1:8088/openapi/v1.json | jq '.paths | keys'

# WebSocket — open http://127.0.0.1:8088/ in two browser tabs and press "broadcast",
# or from a terminal with node ≥ 22:
node -e 'const ws=new WebSocket("ws://127.0.0.1:8088/ws");ws.onmessage=e=>console.log(e.data);ws.onopen=()=>ws.send(JSON.stringify({type:"echo",data:"hi"}))'
```

## REST API

Base path `/api/tasks`. JSON in and out; enums as strings; errors as RFC 9457 `application/problem+json`.

| Method | Path | Success | Errors |
|---|---|---|---|
| `GET` | `/api/tasks?status=Todo&q=postgres&page=1&pageSize=20` | 200 `{items, page, pageSize, total}` | 400 (`q` > 100 chars) |
| `GET` | `/api/tasks/stats?days=14` | 200 `{total, byStatus, completionRate, createdToday, doneThisWeek, donePreviousWeek, oldestOpen, recentlyUpdated[], daily[]}` | 400 (`days` ∉ 1–90) |
| `GET` | `/api/tasks/{id}` | 200 | 404 |
| `POST` | `/api/tasks` `{title, description?}` | 201 + `Location` | 400 validation |
| `PUT` | `/api/tasks/{id}` `{title, description?, status}` | 200 | 400 validation, 404 |
| `DELETE` | `/api/tasks/{id}` | 204 | 404 |
| `GET` | `/health` · `/health/ready` | 200 `Healthy` | 503 |
| `GET` | `/openapi/v1.json` | OpenAPI 3 document | — |

`status` ∈ `Todo | InProgress | Done`. `q` is a case-insensitive substring match on title and description (LIKE
wildcards are escaped). `pageSize` is clamped to `Api:MaxPageSize` (100 by default). `/api/tasks/stats` answers
with three grouped queries (by status, created per day, done per day) — no row is loaded — so a dashboard costs
one request however many tasks exist.

### Catalog — reference data with CRUD (`/api/catalog`)

Every list the portal (part A) used to hard-code — regions, countries, states, places on the map, team members,
links, form options, tags — is a *kind* here. An item has a `code` unique within its kind, a `label`, optional
`parents` (codes of another kind, for cascades), free-form `attributes` (a JSON object, ≤ 4 KB, e.g.
`{"lat": -6.2, "lng": 106.8}`) and a `sort` order.

| Method | Path | Success | Errors |
|---|---|---|---|
| `GET` | `/api/catalog` | 200 `[{kind, count}]` | — |
| `GET` | `/api/catalog/{kind}?parent=asia&q=rus` | 200 `[items]` (≤ 500, by `sort` then `label`) | — |
| `GET` | `/api/catalog/{kind}/{code}` | 200 | 404 |
| `POST` | `/api/catalog/{kind}` `{code?, label, parents?, attributes?, sort?}` | 201 + `Location`; `code` derived from `label` when omitted | 400 validation, 409 duplicate code |
| `PUT` | `/api/catalog/{kind}/{code}` `{label, parents?, attributes?, sort?}` | 200 | 400, 404 |
| `DELETE` | `/api/catalog/{kind}/{code}` | 204 — the code is also removed from every item's `parents` | 404 |

`kind` and `code` match `^[a-z0-9][a-z0-9-]{0,63}$` (anything else is a 404 from routing). One table, a unique
index on `(kind, code)` and a GIN index on `parents` — a dedicated entity per list would have been nine copies of
the same controller. `scripts/seed.sh` seeds the kinds the portal needs and skips a kind that already has items.

### Preferences (`/api/preferences/{userId}`)

`GET` (404 until saved), `PUT {theme: light|dark|system, nickname?}` (upsert, 200), `DELETE` (204). The portal keys
this by the user id in its JWT, so theme and nickname survive a reload and a different browser.

### Uploads (`/api/uploads`)

| Method | Path | Success | Errors |
|---|---|---|---|
| `POST` | `multipart/form-data`: `files[]`, `source?` (tag), `note?` | 201 `[items]` | 400 no file, 413 > `Api:MaxUploadBytes` (2 MB), 415 type not png/jpeg/webp/pdf/txt **or bytes that do not match the declared type** |
| `GET` | `/api/uploads?source=signpad` | 200 newest first (≤ 100) | — |
| `GET` | `/api/uploads/{id}` · `/api/uploads/{id}/content` | 200 metadata · the bytes with the original content type, range requests supported | 404 |
| `DELETE` | `/api/uploads/{id}` | 204 (row and file) | 404 |

Bytes live under `Api:UploadDirectory` — `/var/lib/taskpulse/uploads` via systemd `StateDirectory=` (the only path
the hardened unit can write), a named volume in compose — and only the metadata is in PostgreSQL. Files are stored
under their id, never under the client-supplied name.

**Browsers on another origin:** CORS is off unless `Api:AllowedOrigins` lists the origin
(`Api__AllowedOrigins__0=https://portal.example`; `install.sh` writes it from `TASKPULSE_ALLOWED_ORIGINS="origin ..."`).
Allowed origins get `GET POST PUT DELETE`, the `Content-Type` request header and the `Location` response header — nothing
else, and never `*`. The Vue + Express portal (part A) uses this to show a live task board driven by this API and the
WebSocket server.

**Storage:** PostgreSQL 18 through EF Core (Npgsql). Schema managed by migrations (applied at startup),
`xmin` as an optimistic-concurrency token, `timestamptz` columns, indexes on `status` and `created_at`.
The connection string is the only environment-specific piece — `ConnectionStrings:Tasks`:

| Where | Connection | Auth |
|---|---|---|
| systemd service | `Host=/var/run/postgresql;Port=5433;Database=taskpulse;Username=taskpulse` from `/etc/taskpulse/api.env` (root:root 0600) | **peer** — the OS user *is* the credential, no password exists |
| `dotnet run` / tests | `localhost`, role `taskpulse_dev` (throwaway password, `CREATEDB`) | password, local only |
| Docker compose | `Host=postgres`, `POSTGRES_PASSWORD` from the environment | password, injected |

Nothing is seeded by the database layer: `scripts/seed.sh` writes 40 sample tasks (16 Done, 12 InProgress, 12 Todo) **through the API** (it skips
itself when the table already has rows; `--force` adds the set again), and the VM bootstrap runs it once.
Every response carries `X-Correlation-Id` — send your own to trace a request through the logs.

## WebSocket server

Endpoint `ws://host/ws`. Text frames, JSON both ways.

Client → server: `{"type":"echo"|"broadcast"|"ping","data":"..."}` — anything that is not a JSON
object is treated as `echo` so raw tools (`websocat`, `wscat`) work too.

Server → client:

| `type` | When | Payload |
|---|---|---|
| `welcome` | on connect | `connectionId`, `connections` |
| `echo` | reply to `echo` | `from`, `data` |
| `broadcast` | fan-out to **every** connection | `from`, `data` |
| `pong` | reply to `ping` | `from` |
| `system` | someone joined / left | `event`, `connectionId`, `connections` |
| `error` | bad JSON / unknown type / binary frame | `error` |

`GET /stats` lists live connections. `GET /health` for probes. `GET /` is a dependency-free test page.

Limits: 64 KiB per message (close code 1009 beyond that), 30 s server-side keep-alive pings.

## Design decisions — the "best practices" and why each one is there

**Application**
- **MVC layering, one direction only**: `Controllers` (HTTP in/out, nothing else) → `Services` (rules:
  normalisation, timestamps, paging clamps, logging) → `Repositories` (EF Core queries) → `Data` (context,
  entity, migrations). `Models` holds the API contract (resource, request DTOs, paged envelope). Each layer
  depends on the one below through an interface, so the service is unit-testable with a fake repository and
  the repository can be swapped without touching a controller.
- **Thin `[ApiController]` controllers with declarative validation**: request DTOs carry DataAnnotations,
  the framework returns RFC 9457 `application/problem+json` before an action runs, and `[ProducesResponseType]`
  makes the OpenAPI document list every status code an action can return. Error keys are camel-cased to
  match the JSON contract (`errors.title`, not `errors.Title`).
- **Repository behind an interface** (`ITaskRepository`) with an **EF Core + PostgreSQL** implementation:
  reads are `AsNoTracking`, paging and filtering are SQL, delete is a single `ExecuteDelete`, updates carry
  an `xmin` concurrency token so two racing writers cannot silently overwrite each other, transient
  faults are retried. The storage entity is separate from the API record so the schema can grow without
  touching the contract.
- **Request DTOs separate from the resource**: clients cannot set `id` or timestamps; nullable fields
  so *validation* decides what "missing" means, not the deserializer.
- **Options pattern with `ValidateOnStart`**: a bad `Api:MaxPageSize` fails the process at boot, not
  on the first request. Overridable via environment (`Api__MaxPageSize=200`) — see the units and compose file.
- **problem+json everywhere**: validation failures, unreadable bodies (a bad enum value, truncated JSON)
  and 404s from `NotFound()` all come back as RFC 9457 problem documents, plus a custom `IExceptionHandler`
  so a body Kestrel itself rejects is a 400 in *every* environment.
- **Structured JSON logging** with a correlation-id scope on every line; journald, Docker, Loki and
  Datadog parse it as-is.
- **Liveness vs readiness** (`/health`, `/health/ready`): readiness opens the database, checks for pending
  migrations (→ *Degraded*) and runs a real query. A k8s / load-balancer distinction that costs nothing to
  get right from the start.
- **Correct WebSocket lifecycle**: one send lock per socket (the API allows only one in-flight send),
  frame reassembly up to a hard cap, server-side keep-alive pings, and a *real* graceful shutdown —
  see "Things learned" below.
- **Warnings are errors, nullable enabled, invariant globalization**: cheap discipline; the invariant
  mode is also what lets the runtime image ship without ICU.

**Deployment**
- **Dedicated system user, loopback-only Kestrel, nginx in front**: the apps never face the network
  directly; TLS, rate limiting and access logs belong in the proxy.
- **No database password anywhere**: the service reaches PostgreSQL over the Unix socket and is
  authenticated by its OS identity (`peer`). The connection string sits in a root-only `EnvironmentFile`,
  outside the unit and outside git. Dev/tests use a separate throwaway role.
- **systemd units with hardening**: `systemd-analyze security` scores both services **1.7 (OK)**
  versus 9+ for an unhardened unit. `ProtectSystem=strict`, empty capability set, syscall filter,
  etc. Deliberately **not** `MemoryDenyWriteExecute` — the .NET JIT needs W+X pages.
- **`install.sh` is idempotent and publishes as the invoking user** so the checkout never ends up
  with root-owned `bin/`/`obj/`.
- **nginx WebSocket config**: `proxy_http_version 1.1` + `Upgrade`/`Connection` headers via a `map`,
  and `proxy_read_timeout` raised from the 60 s default so idle sockets are not killed.
- **Multi-stage Docker images**: SDK never ships; runtime layer runs as the non-root `app` user;
  restore is its own cache layer.
- **Smoke test** that exercises the real deployment path (through nginx), not just the code.

## Tests

```
TaskPulse.Api.Tests   26 passed   tasks: CRUD round-trip (re-read after update), data survives a process restart,
                                       concurrent updates, validation 400, bad enum 400, 404, page-size clamp,
                                       q search + LIKE escaping, stats shape and range check, CORS allow-list,
                                       health, correlation id, OpenAPI
                                     catalog: CRUD with derived code and jsonb attributes, 409 on duplicate,
                                       parent filter / search / ordering, delete cascades out of parents, bad input
                                     preferences: upsert + read back, invalid theme, 404 after delete
                                     uploads: round trip incl. downloaded bytes, 413 / 415 (signature mismatch) / 400
TaskPulse.Realtime.Tests   5 passed   welcome/echo/pong, broadcast to two clients, raw text + bad JSON,
                                       plain GET on /ws is 400, /stats + /health
```

All are integration tests through `WebApplicationFactory<Program>` — real routing, JSON, middleware, the real
Npgsql provider against a **throw-away database created per test class on the real server** (dropped after),
and (for the socket) TestServer's in-memory WebSocket client. No mocks, no in-memory database provider.
In CI this is a `postgres` service container and `TASKPULSE_TEST_PG`.

## Things learned while building this (worth asking me about)

1. **A cancelled `ReceiveAsync` aborts the socket.** My first shutdown implementation cancelled the
   receive loop on `ApplicationStopping`; clients saw close code **1006** (abnormal). The socket
   enters *Aborted* and no close frame can be sent. The fix is to send our close frame with
   `CloseOutputAsync` and keep receiving until the peer's close frame arrives. Verified: clients now
   get **1001 "Server shutting down", wasClean=true** on `systemctl restart`, both direct and via nginx.
2. **`location = /health` is an exact match** in nginx — `/health/ready` fell through to the wrong
   upstream. The smoke test caught it.
3. **`systemctl reload nginx` is graceful**, so a test fired immediately afterwards can still hit the
   old workers. Wait for readiness before asserting.
4. **`ThrowOnBadRequest` differs by environment**: the first cut used minimal APIs, where a malformed body
   is 400 in Production and 500 in Development unless you handle `BadHttpRequestException` yourself. The
   test `Update_with_unknown_status_returns_bad_request` caught it; the handler stayed after the move to MVC.
5. **systemd splits an unquoted `Environment=` on whitespace.** `Environment=ConnectionStrings__Tasks=Data Source=…`
   handed the app the value `Data` and the service crash-looped at `MigrateAsync`. Quote the whole assignment.
   `install.sh` now fails loudly with the journal tail instead of waiting on a crash-looping unit.
6. **Port 5432 was already taken** on the demo box by the Node template's PGlite. Ubuntu's installer put the
   cluster on 5433; everything reads the port from `pg_lsclusters` instead of assuming 5432, and the Unix
   socket path includes it (`.s.PGSQL.5433`).
7. **PostgreSQL stores microseconds; .NET ticks are 100 ns.** The `POST` response and the row read back
   differed by a few hundred nanoseconds and a round-trip equality test failed. Timestamps are now generated
   at microsecond precision (`Timestamps.UtcNow()`) so what the API returns is exactly what is stored.
8. **`sudo -S` and a heredoc both want stdin.** A provisioning one-liner silently fed SQL to sudo as the
   password. Not in the shipped scripts (they run as root already) — but it cost twenty minutes.

## What I would add for production

- Migrations as a deploy step (`dotnet ef database update` in the pipeline) rather than at startup, once
  there is more than one instance.
- Return 409 with the current representation on a concurrency conflict instead of 404.
- AuthN/Z (JWT bearer on the API, ticket-based auth on the WebSocket handshake).
- OpenTelemetry traces/metrics export (the `TraceId` is already in every log line).
- Rate limiting (`AddRateLimiter`) and request size limits at the proxy.
- `Type=notify` units via `Microsoft.Extensions.Hosting.Systemd` for true readiness signalling.
- A CI pipeline: build → test → `docker build` → `systemd-analyze verify` on the units.
