param([string]$SptPath = 'D:\SPT')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found.' }
$managed = Join-Path $SptPath 'EscapeFromTarkov_Data\Managed'
$destination = Join-Path $PSScriptRoot 'BepInEx\plugins\SPTPracticeMenu'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$refs = @(
    (Join-Path $SptPath 'BepInEx\core\BepInEx.dll'),
    (Join-Path $SptPath 'BepInEx\core\0Harmony.dll')
)
$refs += Get-ChildItem -LiteralPath $managed -Filter '*.dll' | Select-Object -ExpandProperty FullName
$arguments = @('/nologo', '/noconfig', '/nostdlib+', '/target:library', '/optimize+', '/langversion:5',
    ('/out:' + (Join-Path $destination 'SPTPracticeMenu.dll')))
$arguments += $refs | ForEach-Object { '/reference:' + $_ }
$arguments += Join-Path $PSScriptRoot 'Plugin.cs'
$arguments += Join-Path $PSScriptRoot 'Hotkeys.cs'
$arguments += Join-Path $PSScriptRoot 'AimLogic.cs'
$arguments += Join-Path $PSScriptRoot 'EnemyLabel.cs'
$arguments += Join-Path $PSScriptRoot 'Features.cs'
$arguments += Join-Path $PSScriptRoot 'FeatureRules.cs'
$arguments += Join-Path $PSScriptRoot 'CompactWindow.cs'
$arguments += Join-Path $PSScriptRoot 'AimProfiles.cs'
$arguments += Join-Path $PSScriptRoot 'AimProfileDefaults.cs'
$arguments += Join-Path $PSScriptRoot 'EspRules.cs'
$arguments += Join-Path $PSScriptRoot 'EspRevamp.cs'
$arguments += Join-Path $PSScriptRoot 'LootPresentation.cs'
$arguments += Join-Path $PSScriptRoot 'HoverRules.cs'
$arguments += Join-Path $PSScriptRoot 'PracticeTools.cs'
$arguments += Join-Path $PSScriptRoot 'CombatTools.cs'
$arguments += Join-Path $PSScriptRoot 'CombatMath.cs'
$arguments += Join-Path $PSScriptRoot 'MiscTools.cs'
$arguments += Join-Path $PSScriptRoot 'ItemSpawning.cs'
$arguments += Join-Path $PSScriptRoot 'SpawnRules.cs'
$arguments += Join-Path $PSScriptRoot 'AdvancedPractice.cs'
$arguments += Join-Path $PSScriptRoot 'RaidAwareness.cs'
$arguments += Join-Path $PSScriptRoot 'ExtractionOverrides.cs'
$arguments += Join-Path $PSScriptRoot 'AirdropTools.cs'
$arguments += Join-Path $PSScriptRoot 'NoClipControllerState.cs'
$arguments += Join-Path $PSScriptRoot 'AssemblyInfo.cs'
$arguments += Join-Path $PSScriptRoot 'AimPointRules.cs'
$arguments += Join-Path $PSScriptRoot 'HeadPointResolver.cs'
$arguments += Join-Path $PSScriptRoot 'RageTracking.cs'
$arguments += Join-Path $PSScriptRoot 'PredictionMath.cs'
$arguments += Join-Path $PSScriptRoot 'RageSway.cs'
$arguments += Join-Path $PSScriptRoot 'NewCombatRules.cs'
$arguments += Join-Path $PSScriptRoot 'VampireTools.cs'
$arguments += Join-Path $PSScriptRoot 'DirectHitTools.cs'
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Host "Built $destination\SPTPracticeMenu.dll"
