param(
    [string]$RimWorldDir = 'G:\Steam\steamapps\common\RimWorld',
    [string]$HarmonyPath = 'G:\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll'
)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $modRoot 'build.ps1') -RimWorldDir $RimWorldDir -HarmonyPath $HarmonyPath
if ($LASTEXITCODE -ne 0) { throw 'Mod build failed; probe will not start.' }
& dotnet build (Join-Path $PSScriptRoot 'Probe.csproj') -c Release "-p:RimWorldDir=$RimWorldDir" "-p:HarmonyPath=$HarmonyPath" --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed; probe will not start.' }
$probeDll = Join-Path $PSScriptRoot 'bin\Release\net9.0\WorldTextProbe.dll'
if ([System.Reflection.AssemblyName]::GetAssemblyName($probeDll).Name -ne 'WorldTextProbe') { throw 'Unexpected probe assembly.' }
$stage = Join-Path (Split-Path -Parent $modRoot) 'tmp\world-text-compat-runtime'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Get-ChildItem -LiteralPath $RimWorldDir -File | Where-Object { $_.Extension -in @('.dll','.exe','.json') } | Copy-Item -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $RimWorldDir 'Rendering') -Destination $stage -Recurse -Force
Copy-Item -LiteralPath (Join-Path $RimWorldDir 'RimWorld.dll') -Destination (Join-Path $stage 'Engine.dll') -Force
Copy-Item -LiteralPath $HarmonyPath -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $modRoot '1.7\Assemblies\WorldFeatureTextCompat17.dll') -Destination $stage -Force
Copy-Item -LiteralPath $probeDll -Destination (Join-Path $stage 'RimWorld.dll') -Force
# The copied apphost must run the probe, never the game entry point.
if ([System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $stage 'RimWorld.dll')).Name -ne 'WorldTextProbe') { throw 'Probe replacement verification failed; launch blocked.' }
$stdout = Join-Path $stage 'probe.stdout.log'
$stderr = Join-Path $stage 'probe.stderr.log'
$probe = Start-Process -FilePath (Join-Path $stage 'RimWorld.exe') -WorkingDirectory $stage -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$probe.WaitForExit(30000) | Out-Null
if (!$probe.HasExited) { Stop-Process -Id $probe.Id; throw 'Probe exceeded 30 seconds.' }
Get-Content -LiteralPath $stdout
Get-Content -LiteralPath $stderr
if ($probe.ExitCode -ne 0) { throw "Probe failed: $($probe.ExitCode)" }
Copy-Item -LiteralPath $stdout -Destination (Join-Path $PSScriptRoot 'engine_probe.log') -Force
