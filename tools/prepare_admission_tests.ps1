$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $repository 'tests/UnityFixture'
$game = 'C:/Program Files (x86)/Steam/steamapps/common/MadOut2_BigCityOnline'
New-Item -ItemType Directory -Force (Join-Path $fixture 'Assets/Editor'), (Join-Path $fixture 'Assets/Plugins'), (Join-Path $fixture 'ProjectSettings'), (Join-Path $fixture 'Packages') | Out-Null
Set-Content -LiteralPath (Join-Path $fixture 'ProjectSettings/ProjectVersion.txt') -Value 'm_EditorVersion: 2019.1.8f1' -Encoding ASCII
Set-Content -LiteralPath (Join-Path $fixture 'Packages/manifest.json') -Value '{"dependencies":{}}' -Encoding ASCII
Set-Content -LiteralPath (Join-Path $fixture 'Assets/Editor/MapAdmission.Editor.asmdef') -Value '{"name":"MapAdmission.Editor","includePlatforms":["Editor"]}' -Encoding ASCII
Copy-Item -LiteralPath (Join-Path $repository 'tests/AdmissionLiveFixture.cs') -Destination (Join-Path $fixture 'Assets/Editor/AdmissionLiveFixture.cs') -Force
Copy-Item -LiteralPath (Join-Path $game 'game_Data/Managed/com.unity.multiplayer-hlapi.Runtime.dll') -Destination (Join-Path $fixture 'Assets/Plugins/com.unity.multiplayer-hlapi.Runtime.dll') -Force
