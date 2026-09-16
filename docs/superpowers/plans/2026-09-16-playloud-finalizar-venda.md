# Playloud FinalizarVenda Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce the first application-layer vertical slice so Playloud Cliente can finalize a sale without directly coordinating SQLite, stock mutation, payment validation, or cash ledger logic.

**Architecture:** `Playloud.Application` owns narrow ports and the `FinalizarVenda` use case. `Playloud.Persistence` implements those ports with `SqliteCommerceStore`. `Playloud.Cliente` will later depend on the use case, not on SQLite. Domain invariants remain in `Playloud.Domain`.

**Tech Stack:** C# / .NET 10, xUnit v3 + Microsoft Testing Platform, Microsoft.Data.Sqlite, WPF (consumer only; no WPF changes in this slice).

**Spec:** `docs/superpowers/specs/2026-09-16-playloud-application-use-cases-design.md`

## Global Constraints

- Windows build gate: `.NET 10`, Release, warnings treated as errors.
- TDD is mandatory: RED must be observed before production code.
- `Playloud.Application` must not reference `Playloud.Persistence` or WPF.
- Product identity is always stable `EntityId<Product>`; no array/index identity.
- Persistence must preserve existing atomic sale + stock + payment + cash semantics.
- No partial result when commit fails.

---

### Task 1: Application ports and FinalizarVenda RED

**Files:**
- Create: `tests/Playloud.Application.Tests/FinalizarVendaTests.cs`
- Create: `src/Playloud.Application/Sales/IProductSnapshotReader.cs`
- Create: `src/Playloud.Application/Sales/ISaleCommitter.cs`
- Create: `src/Playloud.Application/Sales/FinalizarVenda.cs`

**Interfaces:**
- Produces: `ProductSnapshot`, `IProductSnapshotReader`, `ISaleCommitter`, `FinalizarVendaCommand`, `FinalizarVendaItem`, `FinalizarVendaPayment`, `FinalizarVendaResult`, `FinalizarVenda`.

- [ ] **Step 1: Write failing tests**

Test four behaviors with in-memory fakes: successful cash sale commits once; mixed payment is preserved; missing product fails before commit; insufficient stock fails before commit.

- [ ] **Step 2: Run CI to verify RED**

Expected: build failure because application contracts/use case do not exist.

- [ ] **Step 3: Implement minimal application contracts and use case**

`FinalizarVenda.ExecuteAsync` loads each product snapshot, builds a domain `Sale`, validates quantity/current stock through domain behavior, completes payment, invokes `ISaleCommitter.CommitSaleAsync` exactly once, then returns id/total/payments.

- [ ] **Step 4: Run CI to verify GREEN**

Expected: application tests pass and full solution regression remains green with 0 warnings/0 errors.

- [ ] **Step 5: Commit**

Commit application contracts and use case together after the GREEN gate.

---

### Task 2: SQLite adapters for application ports

**Files:**
- Modify: `src/Playloud.Persistence/Sqlite/SqliteCommerceStore.cs`
- Test: `tests/Playloud.Persistence.Tests/ApplicationPortAdapterTests.cs`

**Interfaces:**
- Consumes: `IProductSnapshotReader.ReadAsync(EntityId<Product>, CancellationToken)` and `ISaleCommitter.CommitSaleAsync(Sale, EntityId<CashSession>, CancellationToken)`.
- Produces: `SqliteCommerceStore` as an implementation of both ports.

- [ ] **Step 1: Write failing adapter test**

Persist a product/open stock/session, read a `ProductSnapshot` through the application port, complete a domain sale, then commit through `ISaleCommitter` and assert stock/sales/payments/cash results.

- [ ] **Step 2: Run CI to verify RED**

Expected: type/interface implementation failure because `SqliteCommerceStore` does not yet implement application ports.

- [ ] **Step 3: Implement minimal adapters**

Add interface implementation to `SqliteCommerceStore`; reuse existing `GetProductAsync`, `GetStockAsync`, and `CommitSaleAsync` semantics rather than duplicating SQL transaction logic.

- [ ] **Step 4: Run CI to verify GREEN**

Expected: all tests pass with 0 warnings/0 errors.

- [ ] **Step 5: Commit**

Commit adapter integration only after full regression is green.

---

### Task 3: Architecture guard

**Files:**
- Create: `tests/Playloud.Application.Tests/ArchitectureTests.cs`

**Interfaces:**
- Verifies: `Playloud.Application` has no reference to `Playloud.Persistence` and no reference to Windows desktop/WPF assemblies.

- [ ] **Step 1: Write architecture assertion**

Read referenced assemblies from `typeof(Playloud.Application.ModuleMarker).Assembly` and assert names do not include `Playloud.Persistence`, `PresentationFramework`, or `WindowsBase`.

- [ ] **Step 2: Run CI**

Expected: PASS when dependency direction remains correct.

- [ ] **Step 3: Full final gate**

Run `dotnet restore Playloud.sln`, `dotnet build Playloud.sln -c Release --no-restore`, and `dotnet test Playloud.sln -c Release --no-build --no-restore` on Windows GitHub Actions.

Expected: 0 warnings, 0 errors, all tests green.
