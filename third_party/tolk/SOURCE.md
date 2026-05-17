# Tolk — vendored from upstream

[Tolk](https://github.com/dkager/tolk) is a screen reader abstraction
library by Davy Kager. CAMM ships the C# binding (`dotnet/Tolk.cs`) and
the three native runtime DLLs (`dist/x64/*.dll`) consuming launchers
need so that adopters don't have to build Tolk themselves.

## Why vendored, not submoduled

The Tolk upstream repo only contains source; the native DLLs ship as
release artifacts via the project's AppVeyor CI. A submodule would
therefore still leave the DLLs vendored — so the duplication isn't
fully eliminated. Tolk has also been stable for a long time, which
means the "automatic upstream tracking" benefit of a submodule is
small. Vendoring keeps CAMM consumers free of nested-submodule
mechanics (`git clone --recursive` or `submodule update --init
--recursive`) at the cost of manual re-vendoring when Tolk releases.

If Tolk becomes active again or if the heartburn-of-duplication
outweighs the consumer-side simplicity, the right refactor is to
submodule `src/dotnet/Tolk.cs` from upstream while continuing to
vendor the DLLs. Pre-1.0 CAMM has no API stability constraint
preventing that move.

## Upstream snapshot

- Repository: <https://github.com/dkager/tolk>
- License: see `LICENSE.txt` (Tolk itself, LGPL) and `LICENSE-NVDA.txt`
  (the NVDA controller client component).
- C# binding source: <https://github.com/dkager/tolk/blob/master/src/dotnet/Tolk.cs>
- Vendored at: `dotnet/Tolk.cs` (renamed from `src/dotnet/Tolk.cs` —
  flattened because we only ship the .NET binding, not the full source
  tree).
- Native DLLs (`dist/x64/`): sourced from the AppVeyor build artifacts
  of the same upstream commit; CAMM did not build these from source.

## Re-vendoring procedure

When a new Tolk release ships:

1. Replace `dotnet/Tolk.cs` with the upstream `src/dotnet/Tolk.cs`.
2. Download the latest release artifact ZIP from AppVeyor (or the
   project's release page) and replace `dist/x64/Tolk.dll`,
   `dist/x64/nvdaControllerClient64.dll`, `dist/x64/SAAPI64.dll`.
3. Update the "Upstream snapshot" section above with the new commit
   SHA and any notes on what changed.
4. Bump CAMM's version (e.g., 0.0.x → 0.0.x+1) and CHANGELOG entry.
