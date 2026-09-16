# Playloud Application Use-Cases — Design

Date: 2026-09-16
Status: approved by continuation directive

## Goal

Introduce an application layer between WPF and persistence so the UI never coordinates SQLite transactions or domain invariants directly. The first vertical slice is completing a sale through one use case.

## Architecture

Dependency direction:

```text
Playloud.Cliente (WPF)
        |
        v
Playloud.Application
  - use cases
  - ports/interfaces
        ^
        |
Playloud.Persistence
  - SQLite adapter

Playloud.Domain is shared below Application and Persistence.
```

`Playloud.Application` owns the ports. `Playloud.Persistence` references `Playloud.Application` and implements those ports. `Playloud.Application` must not reference `Playloud.Persistence` or WPF.

## First vertical slice: FinalizarVenda

The use case receives:
- business date
- cash session id
- sale lines expressed by stable product ids and quantities
- payment breakdown by method and amount

The use case must:
1. load the current product and stock state through an application port;
2. create the domain `Sale` and add lines using current stock;
3. complete the sale with the supplied payment breakdown;
4. persist the completed sale atomically through the commerce port;
5. return a result containing sale id, gross total and payment summary.

The UI must not decrement stock, calculate cash ledger entries, or open SQL transactions itself.

## Ports

Start with the minimum contracts needed by the vertical slice:

- `IProductSnapshotReader`
  - returns product identity/name/price/cost and current stock for a stable product id.
- `ISaleCommitter`
  - atomically commits a completed sale against one open cash session.

These are intentionally narrower than a large `ICommerceStore` interface so tests and future modules remain isolated.

## Error handling

Application services do not swallow domain/persistence validation failures. Expected business failures remain explicit exceptions for now (invalid quantity, insufficient stock, payment mismatch, closed cash session). The WPF layer will later translate these to user-facing messages.

No partial result is returned when persistence fails.

## Testing

TDD order:
1. RED: successful cash sale produces a completed sale and calls the commit port once.
2. RED: mixed payment is preserved and total must match.
3. RED: missing product fails before persistence.
4. RED: insufficient stock fails before persistence.
5. GREEN: minimal use-case implementation.
6. Adapter tests: SQLite implements the application ports without weakening existing transaction guarantees.
7. Full solution regression on Windows/.NET 10 with warnings treated as errors.

## Non-goals for this slice

- WPF screen redesign
- customer/CRM linkage
- fiscal issuance
- discount rules
- cancellation/refund
- persistence of cash opening/closing from the application layer

Those follow after the sale vertical slice is green.