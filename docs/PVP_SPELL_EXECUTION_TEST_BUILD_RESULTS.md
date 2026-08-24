# PvP 0.5.12 — Test / Build Results

## Version

`0.5.12`

## Current assembly evidence

Supplied current `Assembly-CSharp.dll` SHA-256:

`B840CB8076ED0553F7DC3BEB4042ABA653917882F763181EC0D2C13C26C17847`

`tests/verify_current_assembly_surface.py` result:

- 29 required method targets/overload sets verified;
- 4 reflected NPC cooldown fields verified as float32;
- 6 `CastSpell.StartSpell`-family overloads verified as bool-returning;
- 39 total assembly-surface assertions;
- PASS.

## Portable tests executed in this environment

| Test | Result |
|---|---|
| `verify_current_assembly_surface.py` | PASS — 39 assembly surfaces |
| `verify_pvp_live_execution_source.py` | PASS |
| `verify_pvp_spell_execution_repair.py` | PASS — 58 checks |
| `verify_pvp_stabilization_source.py` | PASS |
| `verify_retained_ui_source.py` | PASS |
| Python syntax compilation for all `tests/*.py` | PASS |

## C# deterministic suite

`tests/PvpUiPolicyTests.cs` currently contains 153 `Assert(...)` calls after this repair. `tests/RUN_UI_TESTS.ps1` compiles/runs that suite and then executes the existing runtime-source guards plus new 0.5.12 spell/heal guards.

This Linux sandbox has no `dotnet`, `csc`, `mcs`, `mono`, `msbuild`, `xbuild`, `pwsh`, or Windows PowerShell runtime, so that Windows/.NET Framework test executable could not be compiled or executed here.

The policy source used by those tests was still exercised indirectly by the portable source validators, but that is **not** presented as equivalent to running the actual C# suite.

## Build result

A fresh `ErenshorPvP.dll` could not be compiled in this Linux sandbox for the same compiler/runtime reason. The pre-existing `build-output/ErenshorPvP.dll` belongs to the incoming snapshot and is not a build of these changes; it is intentionally excluded from the patch packet and no DLL SHA-256 is claimed for 0.5.12 here.

`BUILD_AND_INSTALL.ps1` was hardened so a Windows build/install now runs `tests/RUN_UI_TESTS.ps1` first. If deterministic/source tests fail, it refuses to build/install. The existing safeguards remain:

- compile against the selected live Erenshor `Erenshor_Data/Managed` assemblies plus current Lunaris references;
- compile to a staging DLL first;
- `-BuildOnly` never installs;
- normal install refuses while `Erenshor.exe` is running;
- installed DLL hash must equal the freshly built candidate hash.

Recommended first command on the live Windows machine:

```powershell
.\BUILD_AND_INSTALL.ps1 -GameDir "C:\path\to\Erenshor" -LunarisLibDir "C:\path\to\LunarisLibs" -BuildOnly
```

After it prints a fresh candidate SHA-256 and the tests pass, close Erenshor and rerun without `-BuildOnly` to install. Record that printed SHA-256 as the live candidate hash.

## Release status

Not release-ready. The required remaining gate is live proof of:

`attack AI → concrete native request → bool acceptance → visible/effective cast`

and

`heal decision → legal injured ally/self → native request → actual HP restoration`,

while the already-working movement/melee/world-combat/lifecycle paths remain green.
