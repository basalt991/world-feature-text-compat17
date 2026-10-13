param(
    [string]$RimWorldDir = 'G:\Steam\steamapps\common\RimWorld',
    [string]$HarmonyPath = 'G:\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll'
)
$ErrorActionPreference = 'Stop'
& dotnet build (Join-Path $PSScriptRoot 'Source\WorldFeatureTextCompat.csproj') -c Release "-p:RimWorldDir=$RimWorldDir" "-p:HarmonyPath=$HarmonyPath" --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'World text compatibility build failed.' }
$target = Join-Path $PSScriptRoot '1.7\Assemblies'
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Source\bin\Release\net9.0\WorldFeatureTextCompat17.dll') -Destination $target -Force
Get-FileHash -LiteralPath (Join-Path $target 'WorldFeatureTextCompat17.dll') -Algorithm SHA256
