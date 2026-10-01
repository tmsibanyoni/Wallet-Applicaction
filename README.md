# Wallet

A small wallet service: check a balance, withdraw funds. Built as a take-home exercise, so the
scope is deliberately narrow — one wallet operation done properly rather than a handful done
half way.

Backend is ASP.NET Core (.NET 10) over SQL Server, with a small Angular app on top so there's
something to click on. The interesting part is really in `src/WalletApp.Domain` and
`src/WalletApp.Application` — that's where the "never go negative" rule lives and gets tested.

## What's here

```
src/
  WalletApp.Domain          Wallet aggregate, withdrawal invariants, domain events/exceptions
  WalletApp.Application     Use cases (WalletService), ports (IWalletRepository, IWithdrawalEventBus)
  WalletApp.Infrastructure  EF Core + SQL Server, migrations, event log, in-process event bus
  WalletApp.Api             Controllers, Swagger, config wiring, error mapping
tests/
  WalletApp.Domain.Tests        unit tests on Wallet itself
  WalletApp.Application.Tests   WalletService against mocked ports (incl. concurrency retries)
  WalletApp.Api.Tests           full API against a real, disposable SQL Server database
client/
  wallet-ui                 Angular app: balance + withdraw form
docs/
  architecture.md           diagrams (component view + withdrawal sequence)
```

## Running it locally

You need:
- .NET 10 SDK
- A SQL Server instance reachable at `localhost` with Windows authentication (this is what I
  built against — see [Assumptions](#assumptions) if that doesn't match your machine)
- Node 20+ and npm, for the Angular client

**API**

```bash
dotnet run --project src/WalletApp.Api --launch-profile http
```

On startup the app applies any pending EF Core migrations and creates the database/tables if
they don't exist yet, then makes sure the seed wallet is there. Nothing manual required. It
listens on `http://localhost:5080`; Swagger UI is at `http://localhost:5080/swagger`.

If you want to manage the schema by hand instead:

```bash
dotnet ef database update --project src/WalletApp.Infrastructure --startup-project src/WalletApp.Infrastructure
```

**Client**

```bash
cd client/wallet-ui
npm install
npm start
```

Opens on `http://localhost:4200` and talks to the API at the URL in
`src/environments/environment.ts`. The API's CORS policy is scoped to that origin (see
`Cors:AllowedOrigins` in `appsettings.json`), so if you change the client's port, update both
sides.

**Tests**

```bash
dotnet test
```

The API tests spin up a real, uniquely-named database on your local SQL Server instance per test
class (migrated and seeded the same way the app does at startup) and drop it afterwards. They
need the same local SQL Server the app uses — there's no mock database standing in for it.

## Configuration

Nothing environment-specific is baked into the code. Everything lives in
`src/WalletApp.Api/appsettings.json`:

| Section | Purpose |
|---|---|
| `ConnectionStrings:WalletDb` | SQL Server connection string |
| `Cors:AllowedOrigins` | which origins the API accepts browser requests from |
| `WalletSeed` | the id/owner/balance/currency of the wallet created on first run |
| `Withdrawal:MaxConcurrencyRetries` | how many times to retry a withdrawal after a concurrency conflict before giving up |

## API

Two endpoints, both under `/api/wallets/{walletId}`, where `walletId` is a plain integer (the
seed wallet is `123`, starting at ZAR 1,000.00, matching the example in the brief) rather than a GUID — easier to read, type, and put in a URL for a small
demo like this one. See the trade-off note below on why that's not free.

- `GET /balance` → `{ walletId, balance, currency }`
- `POST /withdrawals` with `{ amount }` → `{ withdrawalId, walletId, amount, balanceAfter, currency, occurredAtUtc }`

Errors come back as [RFC 7807](https://www.rfc-editor.org/rfc/rfc7807) problem details:

| Situation | Status |
|---|---|
| Wallet doesn't exist | 404 |
| Withdrawal amount ≤ 0, or fails model validation | 400 |
| Amount exceeds balance | 409 |
| Concurrency conflict not resolved after retrying | 503 |

Full schema is in Swagger at `/swagger`.

## Design notes

**Where the invariant actually lives.** `Wallet.Withdraw()` is the only way a wallet's balance
changes. It checks the amount is positive, checks funds are sufficient, and only then mutates
the balance — all inside the aggregate, not in a service or a SQL constraint. That means the
"never negative" rule holds no matter what calls it, and it's trivial to unit test without a
database (see `WalletTests.cs`).

**Withdrawal succeeds → balance changes → event exists, all three or none.** The withdrawal
event row (`WithdrawalEvents` table) is written in the *same* `SaveChanges` call as the balance
update, so they commit or roll back together. That's a stripped-down version of the
transactional outbox pattern: I didn't want "the money moved but no event was recorded" to be
possible, even under a crash between the two writes. On top of that durable row, there's a
second, best-effort path — an in-process `Channel<T>` with a background consumer that logs
consumed events — standing in for what would be a real subscriber (fraud checks, notifications,
whatever) reacting to the broker in a production system. If that in-process publish fails, the
withdrawal still succeeds, because the durable row is the actual source of truth, not the live
notification.

**Concurrency: optimistic, with a retry loop, over a real database.** `Wallet.Version` is an EF
Core concurrency token. Two concurrent withdrawals against the same wallet will not both apply —
the second one's `UPDATE ... WHERE Version = @loaded` affects zero rows, EF throws
`DbUpdateConcurrencyException`, and `WalletService` catches that, reloads the wallet, and retries
the whole operation against the fresh balance. `tests/WalletApp.Api.Tests/WithdrawalConcurrencyTests.cs`
fires 20 concurrent withdrawal requests at a wallet with only enough balance for 5, against the
real API and a real database, and asserts exactly 5 succeed and the final balance is precisely
zero — not close to zero, not negative.

I originally built this against SQLite for a zero-install local setup, and hit exactly the
locking behaviour you'd expect from a single-writer embedded database under concurrent requests.
Rather than paper over that with pragmas, I moved to the SQL Server instance already running on
this machine, which handles row-level locking properly and made the concurrency test both
correct and fast. It also meant switching from `EnsureCreated` to real EF Core migrations
(`src/WalletApp.Infrastructure/Persistence/Migrations`), which is the right call anyway — schema
changes should be reviewable, not implicit.

One bug this caught, worth mentioning because it wasn't obvious going in: `WalletDbContext` is
scoped per request, and EF's identity map means a second `GetByIdAsync` call *within that same
context* was returning the same, already-mutated in-memory `Wallet` instead of a fresh read —
so the retry loop was silently retrying against stale data instead of the current row. Fixed by
explicitly reloading the tracked entity (and detaching the failed event insert) on a concurrency
conflict, in `EfWalletRepository.SaveWithdrawalAsync`. The concurrency test is what surfaced it —
it failed with a suspiciously small success count before the fix.

**Layering.** Domain has no dependencies on anything. Application depends only on Domain and
defines the ports (`IWalletRepository`, `IWithdrawalEventBus`) it needs; it's tested entirely
with mocks, no database involved. Infrastructure implements those ports against EF Core /
SQL Server. Api wires it all together and is the only layer that knows about HTTP. Nothing here
is over-abstracted for the sake of it — there's exactly one use case (withdraw) needing
persistence, so the repository has exactly one purpose-built save method rather than generic
CRUD.

## Assumptions

- **A wallet already exists.** The brief says a creation endpoint isn't required, so the app
  guarantees one wallet on startup (id, owner, balance, currency all configurable in
  `appsettings.json`, not hard-coded). There's no way to create additional wallets through the
  API — that's intentionally out of scope.
- **SQL Server on localhost, Windows auth.** That's what's on this machine, so that's what I
  built and tested against. If you're running this somewhere else, change
  `ConnectionStrings:WalletDb` (e.g. to a SQL login, or to a `(localdb)\MSSQLLocalDB` instance).
- **The client knows the wallet id out of band.** There's no "list wallets" endpoint (matches
  "no wallet creation" in scope), so the Angular app just defaults to the seeded wallet's id from
  its own environment config, with an input if you want to query a different (or non-existent, to
  see the 404 path) id.
- **Currency is an opaque string**, not a real multi-currency concept. No FX, per the brief.
- **No auth.** Also explicitly out of scope. Anyone who can reach the API and knows a wallet id
  can withdraw from it — see limitations below.

## Trade-offs

- **SQL Server over SQLite/in-memory providers** — heavier to set up than an embedded database,
  but it's what made the concurrency behaviour (and the concurrency test) actually meaningful
  instead of hand-waved.
- **In-process event bus over standing up a real broker.** A real system would use RabbitMQ /
  Kafka / a cloud queue with a proper outbox relay. For this exercise, adding infrastructure the
  reviewer would have to also install and run felt like the wrong trade — the durable
  `WithdrawalEvents` row plus a swappable `IWithdrawalEventBus` interface gets the design point
  across without it.
- **Optimistic concurrency + retries over pessimistic locking.** Simpler, and fine for what's
  presumably light contention on any one wallet in practice. The retry budget
  (`Withdrawal:MaxConcurrencyRetries`, default 25) is generous enough to resolve a 20-way burst
  in tests; a real system would probably use a smaller server-side budget plus a `503` +
  `Retry-After` and let the client back off, rather than holding the request open through a long
  retry chain.
- **Swagger always on, not gated to `Development`.** Convenient for this exercise; I'd restrict
  or remove it in a real deployment.
- **Short integer wallet ids over GUIDs.** Started with GUIDs (the usual choice when you don't
  want ids to be guessable or enumerable), then switched to a plain `int` because typing/copying
  `11111111-1111-1111-1111-111111111111` around while testing was needless friction for a
  single-wallet demo. The real cost of that switch: combined with "no auth" below, a short
  sequential id is trivially guessable/enumerable, which a GUID would have at least made
  impractical. Worth reverting to GUIDs (or adding auth) before this is anything but a demo.

## Known limitations

- No authentication/authorization at all — out of scope per the brief, but worth being explicit
  that this is not close to production-shaped as-is. Combined with short integer wallet ids (see
  trade-offs), wallet ids are also easy to guess/enumerate — a GUID plus auth would be the
  realistic combination, not GUID alone or auth alone.
- No idempotency key on the withdrawal endpoint. A client retrying a timed-out request (rather
  than a genuinely new withdrawal) can currently cause a double withdrawal.
- No audit/history endpoint, even though the data exists — `WithdrawalEvents` is written but
  nothing reads it back through the API yet.
- No rate limiting or request throttling.
- The Angular client is intentionally minimal: one wallet at a time, no routing, no styling
  system, and only component/service-level tests (no e2e).

## Potential improvements

- Swap `InProcessWithdrawalEventBus` for a real broker publisher, with a background dispatcher
  relaying undelivered `WithdrawalEvents` rows and marking them sent — the interface is already
  shaped for this.
- Idempotency keys on `POST /withdrawals`.
- A `GET /wallets/{id}/withdrawals` endpoint over the existing event log.
- JWT-based auth with wallet ownership checks.
- A proper `Money` value object if multi-currency ever comes into scope.
- CI (GitHub Actions) running `dotnet test` and the Angular test suite on every push.
- Health check endpoint, structured logging/tracing.

## AI usage

Thabang Sibanyoni did the design and made the decisions on this project. Claude (Anthropic's AI
assistant) helped with the implementation, working from the instructions and commands Thabang
gave it, and the work was reviewed and run locally before being committed.

Commits that include Claude's work carry a `Co-Authored-By: Claude` trailer, so the history shows
where it helped.
