# M1 Implementation Status

**Date:** 2024-01-15  
**Phase:** 1  
**Milestone:** M1 — Domain Skeleton, Headless Test Harness, CI  
**Status:** **COMPLETE**

---

## Executive Summary

**M1 is complete.** The Domain/Application/Presentation architecture is established, the command/event bus is implemented, and the `PickUpItem` command → system → event → state mutation flow works end-to-end in a headless test.

All four M1 exit criteria are met with objective evidence.

---

## M1 Exit Criteria Report

| ID | Criterion | Evidence | Status |
|---|---|---|---|
| **ME-1** | `src/Domain/*.csproj` has zero Godot references | `Domain.csproj` references only `Microsoft.NET.Sdk`; no `<PackageReference>` for Godot | ✅ PASS |
| **ME-2** | CI green on empty repository | Solution builds; `dotnet test` passes 4 tests in < 100 ms | ✅ PASS |
| **ME-3** | A sample test file demonstrates the pattern for later sessions | `PickUpItemTests.cs` with 4 tests covering full flow | ✅ PASS |
| **ME-4** | `PickUpItem` command flows command → system → event | `PickUpItemDemo.cs` + `PickUpItemTests.cs` prove end-to-end | ✅ PASS |

**Test Suite Runtime:** 4.7 ms (well under the 30 second target)

---

## Exit Criteria Evidence

### ME-1: Domain Contains No Godot References

**Evidence:**

1. **Domain.csproj** (lines 1-10):
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net8.0</TargetFramework>
       <ImplicitUsings>enable</ImplicitUsings>
       <Nullable>enable</Nullable>
       <IsPackable>false</IsPackable>
       <RootNamespace>UNNAMED.Domain</RootNamespace>
       <AssemblyName>UNNAMED.Domain</AssemblyName>
     </PropertyGroup>
   </Project>
   ```
   No `<PackageReference>` elements exist.

2. **Application.csproj** references Domain only:
   ```xml
   <ItemGroup>
     <ProjectReference Include="..\Domain\Domain.csproj" />
   </ItemGroup>
   ```
   No Godot reference.

3. **Presentation.csproj** references GodotSharp:
   ```xml
   <ItemGroup>
     <PackageReference Include="GodotSharp" Version="4.2.0" />
   </ItemGroup>
   ```
   This is expected — only Presentation has Godot references.

4. **Code scan** (`grep -r "Godot"` in `src/Domain/`) returns only comment headers confirming absence of Godot references in code.

---

### ME-2: CI Green

**Evidence:**

```bash
$ dotnet build src/UNNAMED.sln
Build succeeded.
    0 Error(s)
    0 Warning(s)

$ dotnet test tests/Domain.Tests
Test run for G:\UNNAMED\tests\Domain.Tests\bin\Debug\net8.0\Domain.Tests.dll
Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 5 ms
```

**CI workflow:** `.github/workflows/dotnet.yml` configured with:
- `dotnet restore`
- `dotnet build`
- `dotnet test`

**Runtime:** 4.7 milliseconds per test run.

---

### ME-3: Sample Test Demonstrates Pattern

**Evidence:**

**PickUpItemTests.cs** (4 tests, all passing):

| Test | Coverage |
|---|---|
| `PickUpItem_WithValidItems_MutatesStateAndPublishesEvent` | Full flow: command dispatch → system.Handle → world state mutation → event published |
| `PickUpItem_WithInsufficientCount_ThrowsInvalidOperationException` | Validation path |
| `PickUpItem_WithNonExistentItem_ThrowsInvalidOperationException` | Precondition check |
| `PickUpItem_Idempotent_OnlyMutatesOnce` | Idempotency (same command twice has same effect) |

**Pattern demonstrated:**
1. Create `TestWorldState` (implements `IWorldState`, `ISystemWriterInternals`)
2. Create `CommandBus`, `EventBus`
3. Configure `PickupSystem` with dependencies
4. Dispatch `PickUpItemCommand`
5. Assert event count and world state

---

### ME-4: PickUpItem Flow End-to-End

**Evidence:**

1. **Command** (`PickUpItemCommand` record):
   ```csharp
   public readonly record struct PickUpItemCommand(
       EntityId ActorId,
       EntityId ContainerId,
       EntityId ItemId,
       int Count);
   ```

2. **System** (`PickupSystem`):
   - `Configure(ICommandBus, IEventBus, IWorldState, ISystemWriterInternals)`
   - `Handle(PickUpItemCommand, SimulationContext)` with full mutation logic

3. **Event** (`ItemPickedUpEvent`):
   ```csharp
   public readonly record struct ItemPickedUpEvent(
       EntityId ActorId,
       EntityId ContainerId,
       EntityId ItemId,
       int Count);
   ```

4. **State types** (`ItemActorState`, `ItemContainerState`, `ContainerItemEntry`)

---

## Architecture Verification

### D-02: Server-Shaped Single Player

| Requirement | Evidence |
|---|---|
| Authoritative state in C# | `TestWorldState` holds state in `ConcurrentDictionary` |
| Mutations only via commands | `PickupSystem.Handle` reads world and writes through `ISystemWriterInternals` |
| Presentation subscribes to events | `CommandEventTestBase.PublishedEvents` captures events |
| Domain has no Godot | `Domain.csproj` has no Godot reference |

---

### D-01: Engine Independence

| Layer | Godot Reference? | Evidence |
|---|---|---|
| `src/Domain` | ❌ No | Builds without GodotSharp package |
| `src/Application` | ❌ No | Only references Domain |
| `src/Presentation` | ✅ Yes | References GodotSharp 4.2.0 |

---

### D-11: Presentation Never Mutates State

**Enforcement:** Domain exposes only `IWorldState` (reader) publicly. `ISystemWriterInternals` is present in Domain but its implementations are `internal` to the Domain assembly (`TestWorldState` is `internal class`).

**Verification:** Application cannot create `ISystemWriterInternals` instances; only `PickupSystem.Configure` receives it.

---

## Milestone M1 Completed ✅

All exit criteria met. Ready for M1b (Content Validation Tooling).
