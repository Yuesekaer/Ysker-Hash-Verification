# Ysker-Hash-Verification - build script
# (kept ASCII-only on purpose: Windows PowerShell 5.1 reads BOM-less .ps1 files as ANSI.)

param(
    [string]$OutDir = "",
    [string]$Icon = "",                 # empty => assets\MD5.ico next to this script
    [string]$Sdk = "",
    [string]$RefAsm = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $root 'dist' }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
$OutDir = (Resolve-Path $OutDir).Path
if ([string]::IsNullOrWhiteSpace($Icon)) { $Icon = Join-Path $root 'assets\MD5.ico' }

# ---- 1) locate the Roslyn compiler inside the dotnet SDK ----
if ([string]::IsNullOrWhiteSpace($Sdk)) {
    $sdkRoot = 'C:\Program Files\dotnet\sdk'
    if (-not (Test-Path $sdkRoot)) { throw "dotnet SDK folder not found: $sdkRoot (pass -Sdk <dir>)" }
    $Sdk = (Get-ChildItem $sdkRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
}
$csc = Join-Path $Sdk 'Roslyn\bincore\csc.exe'
if (-not (Test-Path $csc)) { throw "Roslyn compiler not found: $csc" }

# ---- 2) locate .NET Framework reference assemblies ----
if (-not (Test-Path (Join-Path $RefAsm 'System.Windows.Forms.dll'))) {
    $base = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework'
    if (-not (Test-Path $base)) {
        throw ("reference assemblies not found under: $base`n" +
               "  Install 'Visual Studio Build Tools' (with the .NET Framework 4.x targeting pack)`n" +
               "  or the Windows SDK, then re-run. You can also pass -RefAsm <dir>.")
    }
    $RefAsm = (Get-ChildItem $base -Directory |
               Where-Object { $_.Name -like 'v4.*' -and $_.Name -ne 'v4.X' } |
               Sort-Object Name -Descending | Select-Object -First 1).FullName
}
if (-not (Test-Path (Join-Path $RefAsm 'System.Windows.Forms.dll'))) {
    throw "no System.Windows.Forms.dll in reference assembly folder: $RefAsm"
}

# ---- 3) sources ----
$src = @(
    (Join-Path $root 'src\AssemblyInfo.cs'),   # assembly metadata (English product name)
    (Join-Path $root 'src\YskerCore.cs'),      # palette / metrics / card renderer (shared with preview)
    (Join-Path $root 'src\TitleBar.cs'),       # self-drawn title bar (no native frame)
    (Join-Path $root 'src\PreviewShot.cs'),    # design-preview generator (used by --preview)
    (Join-Path $root 'src\YskerMd5.cs')        # main program: UI + logic + self test
)
foreach ($f in $src) { if (-not (Test-Path $f)) { throw "missing source file: $f" } }

# ---- 4) keep the icon inside the repo so a build only needs the source tree ----
$assets = Join-Path $root 'assets'
if (-not (Test-Path $assets)) { New-Item -ItemType Directory -Force -Path $assets | Out-Null }
$localIcon = Join-Path $assets 'MD5.ico'
if (Test-Path $Icon) { try { Copy-Item $Icon $localIcon -Force } catch { } }
elseif (Test-Path $localIcon) { $Icon = $localIcon }
else { $Icon = "" }

# ---- 5) compile ----
$exe = Join-Path $OutDir 'Ysker-Hash-Verification.exe'
$refs = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll') |
        ForEach-Object { "/r:$(Join-Path $RefAsm $_)" }

$cscArgs = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001') +
           $refs + @("/out:$exe")
if (-not [string]::IsNullOrWhiteSpace($Icon) -and (Test-Path $Icon)) {
    $cscArgs += "/win32icon:$Icon"
    try { Copy-Item $Icon (Join-Path $OutDir 'MD5.ico') -Force } catch { }
}
$cscArgs += $src

$sw = [Diagnostics.Stopwatch]::StartNew()
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "compile failed (csc exit code $LASTEXITCODE)" }
$sw.Stop()

$len = (Get-Item $exe).Length
if ($len -lt 16384) { throw "unexpected output size: $len bytes" }

# ---- 6) hash self-test (fast, worth running after every build) ----
$rep = Join-Path $OutDir 'selftest.txt'
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.Arguments = "--selftest `"$rep`""
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$sp = [System.Diagnostics.Process]::Start($psi)
if (-not $sp.WaitForExit(120000)) { try { $sp.Kill() } catch { } ; Write-Warning "self-test timed out" }
elseif ($sp.ExitCode -ne 0) { Write-Warning "self-test process exit code $($sp.ExitCode)" }

Write-Host ""
Write-Host ("Built : {0}" -f $exe)
Write-Host ("        {0:N0} bytes   {1:N1} s" -f $len, $sw.Elapsed.TotalSeconds)
Write-Host ("        ref assemblies {0}   icon {1}" -f (Split-Path $RefAsm -Leaf), $(if ($Icon) { Split-Path $Icon -Leaf } else { '(none)' }))
if (Test-Path $rep) {
    Write-Host ""
    Get-Content $rep | Select-Object -First 3 | ForEach-Object { Write-Host "        $_" }
}
