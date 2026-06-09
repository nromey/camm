<#
.SYNOPSIS
  Build prism.dll from the pinned Prism submodule (camm/third_party/prism).

.DESCRIPTION
  Invoked by Camm.Prism.targets during a launcher build when
  CammPrismMode=BuildFromSource. Mirrors how the Tolk binding is consumed,
  except Prism is BUILT from source (it iterates weekly; a committed binary
  would go stale). See camm/build/PRISM.md.

  Self-contained: locates Visual Studio via vswhere, loads vcvars64 (so
  cl.exe / lib.exe are on PATH -- the configure step generates import
  libraries and needs lib.exe), and drives CMake + Ninja. CMake and Ninja
  are taken from PATH when present (so CI can inject a newer CMake) and
  otherwise from the VS-bundled copies.

  Incremental: records the source commit in <Out>\prism.dll.builtfrom and
  skips the (slow, ~2 min) C++ build when prism.dll is already built from
  the current submodule commit. Bumping the submodule pin forces a rebuild.

  Builds OUT OF SOURCE to a dir OUTSIDE the submodule so the submodule
  working tree never goes dirty (a dirty submodule failed the v0.6.0
  release).

  ASCII-only on purpose: MSBuild's Exec runs this under Windows PowerShell
  5.1, which reads the file as the ANSI codepage, so non-ASCII bytes
  (em-dashes, arrows) would corrupt the parse.

.PARAMETER Source
  Path to the Prism submodule checkout (contains CMakeLists.txt).

.PARAMETER Out
  Directory to place the built prism.dll (+ the .builtfrom stamp).

.PARAMETER BuildDir
  CMake build directory. Defaults to <Out>\..\prism-build. Must be OUTSIDE
  the submodule.

.PARAMETER Force
  Rebuild even if the stamp says it is up to date.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)] [string] $Source,
  [Parameter(Mandatory = $true)] [string] $Out,
  [string] $BuildDir = "",
  [switch] $Force
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path (Join-Path $Source "CMakeLists.txt"))) {
  throw "Prism source not found at '$Source'. The submodule is not initialized. Run: git submodule update --init --recursive"
}
$Source = (Resolve-Path $Source).Path
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path $Out).Path
if (-not $BuildDir) { $BuildDir = Join-Path (Split-Path $Out -Parent) "prism-build" }

$dll   = Join-Path $Out "prism.dll"
$stamp = Join-Path $Out "prism.dll.builtfrom"

# Source commit, for the incremental skip.
$sha = (& git -C $Source rev-parse HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sha)) { $sha = "unknown" }

if (-not $Force -and (Test-Path $dll) -and (Test-Path $stamp) -and
    ((Get-Content $stamp -Raw).Trim() -eq $sha.Trim())) {
  Write-Host "[build-prism] prism.dll already built from this commit; skipping."
  exit 0
}

# Locate Visual Studio (for vcvars64 + bundled CMake/Ninja fallback).
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
  throw "vswhere not found at '$vswhere'. Visual Studio with the C++ workload is required to build Prism."
}
$vsPath = (& $vswhere -latest -products * `
  -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
  -property installationPath) | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($vsPath)) {
  throw "No Visual Studio install with the C++ (VC.Tools.x86.x64) workload was found."
}
$vcvars = Join-Path $vsPath "VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path $vcvars)) { throw "vcvars64.bat not found at '$vcvars'." }

# CMake / Ninja: prefer PATH (CI can supply a newer CMake), else VS-bundled.
$cmake = (Get-Command cmake -ErrorAction SilentlyContinue).Source
if (-not $cmake) {
  $cmake = Join-Path $vsPath "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
}
if (-not (Test-Path $cmake)) { throw "cmake not found on PATH or in the VS install." }

$ninja = (Get-Command ninja -ErrorAction SilentlyContinue).Source
if (-not $ninja) {
  $ninja = Join-Path $vsPath "Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"
}
if (-not (Test-Path $ninja)) { throw "ninja not found on PATH or in the VS install." }

Write-Host "[build-prism] VS:    $vsPath"
Write-Host "[build-prism] cmake: $cmake"
Write-Host "[build-prism] ninja: $ninja"
Write-Host "[build-prism] building prism.dll (source commit $sha)"
Write-Host "[build-prism]   source:   $Source"
Write-Host "[build-prism]   builddir: $BuildDir"

# Run "<vcvars> && <cmake ...>" through cmd so cl.exe / lib.exe land on
# PATH for the configure step's import-library generation. Single
# interpolated strings (backtick-escaped quotes) parse cleanly under both
# Windows PowerShell 5.1 and PowerShell 7.
#
# Ninja single-config build. GDExtension OFF avoids the large godot-cpp
# fetch; tests/demos OFF. BUILD_SHARED_LIBS=ON yields prism.dll.
$configure = "`"$vcvars`" && `"$cmake`" -S `"$Source`" -B `"$BuildDir`" -G Ninja -DCMAKE_MAKE_PROGRAM=`"$ninja`" -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=ON -DPRISM_ENABLE_GDEXTENSION=OFF -DPRISM_ENABLE_TESTS=OFF -DPRISM_ENABLE_DEMOS=OFF"
cmd /c $configure
if ($LASTEXITCODE -ne 0) { throw "CMake configure failed (exit $LASTEXITCODE)." }

$build = "`"$vcvars`" && `"$cmake`" --build `"$BuildDir`" --target prism"
cmd /c $build
if ($LASTEXITCODE -ne 0) { throw "CMake build failed (exit $LASTEXITCODE)." }

$built = Get-ChildItem -Path $BuildDir -Recurse -Filter prism.dll -ErrorAction SilentlyContinue |
         Select-Object -First 1
if (-not $built) { throw "Build reported success but prism.dll was not found under '$BuildDir'." }

Copy-Item $built.FullName $dll -Force
Set-Content -Path $stamp -Value $sha -NoNewline
Write-Host "[build-prism] done -> $dll"
