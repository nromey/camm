# Prism backend — build from source

CAMM offers two screen-reader output backends behind `IScreenReader`:
**Tolk** (default; vendored prebuilt binaries in `third_party/tolk/`) and
**Prism** ([ethindp/prism](https://github.com/ethindp/prism), MPL-2.0).

Unlike Tolk — which is effectively frozen, so a committed binary never
goes stale — Prism iterates weekly and is pre-1.0. So Prism is **built
from source** rather than committed as a binary:

- `third_party/prism/` is a **pinned git submodule** of upstream Prism.
  The pin keeps release tags reproducible; bump it deliberately to take a
  newer Prism (`git submodule update --remote third_party/prism`, rebuild,
  test, commit the new gitlink).
- `build/build-prism.ps1` builds `prism.dll` from that submodule.
- `build/Camm.Prism.targets` runs the build during the launcher build and
  embeds the result as the `prism/prism.dll` resource.

## Adopter usage

In the launcher `.csproj`:

```xml
<Import Project="..\camm\build\Camm.Prism.targets" />
<PropertyGroup>
  <CammPrismMode>BuildFromSource</CammPrismMode>
</PropertyGroup>
```

`CammPrismMode` values:

| Mode | Behavior |
| --- | --- |
| `None` (default) | Don't bundle Prism. Tolk-only launcher. |
| `BuildFromSource` | Build `prism.dll` from the pinned submodule during this build (incremental) and embed it. Needs a C++ toolchain. |
| `Prebuilt` | Embed an existing `prism.dll` as-is (`CammPrismPrebuiltDll`). For toolchain-less environments. |

Bundling Tolk is independent (keep embedding `tolk/*` in the launcher
csproj). An adopter ships Tolk, Prism, or both. *Which* backend is used at
runtime is `CammModManifest.ScreenReaderBackend` (default Tolk), overridable
for testing via the `CAMM_SCREEN_READER_BACKEND` env var (`tolk`/`prism`).

## Toolchain

`build-prism.ps1` locates Visual Studio via `vswhere`, loads `vcvars64`
(the configure step generates import libraries and needs `lib.exe`), and
drives CMake + Ninja (PATH copies if present, else the VS-bundled ones).
Prism requires C++23 — use a recent toolchain:

- **Locally:** VS 2026 (MSVC 19.51) + its bundled CMake 4.2 builds it
  cleanly. VS 2022's older bundled CMake (3.31) does **not** know the MSVC
  C23 dialect flag and fails configure — supply a newer CMake on PATH if
  you only have VS 2022.
- **CI:** the GitHub `windows` runner loads MSVC via `ilammy/msvc-dev-cmd`;
  ensure a recent CMake is on PATH (add a setup-cmake step if the bundled
  one is too old for C23).

Build flags: `-DBUILD_SHARED_LIBS=ON` (→ `prism.dll`),
`-DPRISM_ENABLE_GDEXTENSION=OFF` (skips the godot-cpp fetch),
`-DPRISM_ENABLE_TESTS=OFF -DPRISM_ENABLE_DEMOS=OFF`. The result statically
links the CRT (no VC++ redist dependency) and delay-loads the per-reader
client DLLs (ZDSR / PCTalker / BoyPCReader / Orca / speech-dispatcher), so
it loads cleanly on a machine with only NVDA / SAPI / JAWS / OneCore / UIA.

## Current pin

`third_party/prism` → upstream `master` @ `bb68308`
(v0.16.5-6-gbb68308), the commit validated against NVDA on 2026-06-09.
