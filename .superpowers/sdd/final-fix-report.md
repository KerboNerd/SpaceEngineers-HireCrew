# Final Important fixes report

Date: 2026-07-26

## Notes

Addressed all Important findings from the HireCrew final whole-branch review in one pass (`CrewSession.cs`, `NpcSeater.cs`):

1. **WC gate when API not ready** — `HandleAssign` rejects with `"WeaponCore not ready"` when `WeaponAi == null || !WeaponAi.IsReady`; only then requires `IsCoreWeapon(weapon)`.
2. **Grid-split GridEntityId drift** — `WatchCrewIntegrity` no longer invalidates solely because the stored grid entity vanished. When seat+weapon remain valid on the same cube grid but `crew.GridEntityId` differs, rekeys to the seat grid, Upserts, and `BroadcastRoster`s old and new grid ids.
3. **Null RPC guards** — `HandleHire` / `HandleAssign` / `HandleDismiss` return immediately if `req == null`.
4. **FindNearbyCharacter fallback** — If `SpawnBot` returns a non-zero id that cannot be resolved, TrySeat fails (despawns id) instead of scanning. Nearby scan is last-resort only when spawn id is 0, radius `<1.0m`, excludes Store-tracked `CharacterEntityId`s, and fails on ambiguity. Call sites pass `CollectKnownCharacterIds()`.
5. **Tier clamp** — Invalid `HireRequest.Tier` clamped to `Recruit`..`Elite` before pricing/hire.

SE launch skipped (per request).

## Tests

Command:

```
dotnet test tests\HireCrew.Logic.Tests\HireCrew.Logic.Tests.csproj -v n
```

Result:

```
Test Run Successful.
Total tests: 10
     Passed: 10
 Total time: 3.3873 Seconds

Build succeeded.
    0 Warning(s)
    0 Error(s)
```
