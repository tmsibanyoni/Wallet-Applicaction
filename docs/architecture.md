# Architecture

## Components

Domain has zero dependencies. Application depends only on Domain and defines the ports it needs.
Infrastructure and Api both implement/consume those ports — Api never talks to EF Core directly,
and Infrastructure never talks to HTTP.

```mermaid
flowchart TB
    subgraph Client
        UI["Angular app<br/>(client/wallet-ui)"]
    end

    subgraph Api["WalletApp.Api"]
        Controller["WalletsController"]
        ExHandler["ApiExceptionHandler<br/>(-> ProblemDetails)"]
    end

    subgraph App["WalletApp.Application"]
        Service["WalletService<br/>(GetBalance / Withdraw)"]
        Ports["Ports:<br/>IWalletRepository<br/>IWithdrawalEventBus"]
    end

    subgraph Domain["WalletApp.Domain"]
        Wallet["Wallet aggregate<br/>(Withdraw invariant)"]
    end

    subgraph Infra["WalletApp.Infrastructure"]
        Repo["EfWalletRepository"]
        DbCtx["WalletDbContext (EF Core)"]
        Bus["InProcessWithdrawalEventBus<br/>(Channel&lt;T&gt;)"]
        Consumer["WithdrawalEventLoggingConsumer<br/>(BackgroundService)"]
    end

    DB[("SQL Server<br/>Wallets / WithdrawalEvents")]

    UI -->|HTTP JSON| Controller
    Controller --> Service
    Controller -.->|on exception| ExHandler
    Service --> Wallet
    Service --> Ports
    Ports -. implemented by .-> Repo
    Ports -. implemented by .-> Bus
    Repo --> DbCtx --> DB
    Bus --> Consumer
```

## Withdrawal sequence

The durable event write and the balance update happen in the *same* `SaveChanges` call — one
transaction, so "money moved" and "event recorded" can't disagree. The live event-bus publish
happens only after that transaction has already committed, and is best-effort.

```mermaid
sequenceDiagram
    participant C as Client
    participant Ctrl as WalletsController
    participant Svc as WalletService
    participant W as Wallet (aggregate)
    participant Repo as EfWalletRepository
    participant DB as SQL Server
    participant Bus as InProcessWithdrawalEventBus

    C->>Ctrl: POST /wallets/{id}/withdrawals { amount }
    Ctrl->>Svc: WithdrawAsync(id, amount)
    Svc->>Repo: GetByIdAsync(id)
    Repo->>DB: SELECT wallet WHERE Id = @id
    DB-->>Repo: wallet row (incl. Version)
    Repo-->>Svc: Wallet

    Svc->>W: Withdraw(amount)
    alt amount <= 0
        W-->>Svc: throws ArgumentOutOfRangeException
        Svc-->>Ctrl: (propagates)
        Ctrl-->>C: 400 ProblemDetails
    else amount > balance
        W-->>Svc: throws InsufficientFundsException
        Svc-->>Ctrl: (propagates)
        Ctrl-->>C: 409 ProblemDetails
    else sufficient funds
        W-->>Svc: WithdrawalCompleted event (balance mutated in memory)

        Svc->>Repo: SaveWithdrawalAsync(wallet, event)
        Repo->>DB: UPDATE Wallets SET Balance=.., Version=..<br/>WHERE Id=@id AND Version=@loaded
        Repo->>DB: INSERT INTO WithdrawalEvents (...)
        Note over Repo,DB: both in one transaction

        alt no concurrent writer (0 rows was not the case)
            DB-->>Repo: success
            Repo-->>Svc: OK
            Svc->>Bus: PublishAsync(event)  (best effort)
            Svc-->>Ctrl: WithdrawResult
            Ctrl-->>C: 200 { balanceAfter, ... }
        else another request updated Version first
            DB-->>Repo: 0 rows affected
            Repo->>Repo: reload wallet to current DB state,<br/>detach the failed event insert
            Repo-->>Svc: throws ConcurrencyConflictException
            Svc->>Svc: retry (re-run from GetByIdAsync)<br/>up to Withdrawal:MaxConcurrencyRetries
        end
    end
```
