param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $repositoryRoot 'artifacts/OneTapRecorder' }
$projectFile = Join-Path $repositoryRoot 'src/OneTapRecorder/OneTapRecorder.csproj'
dotnet publish $projectFile -c Release --self-contained true -o $OutputDirectory -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination (Join-Path $OutputDirectory 'README.md')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.zh-Hant.md') -Destination (Join-Path $OutputDirectory 'README.zh-Hant.md')
Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'licenses') -File | Copy-Item -Destination $OutputDirectory
foreach ($folder in @('docs', 'licenses')) {
    $destination = Join-Path $OutputDirectory $folder
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot $folder) -File | Copy-Item -Destination $destination
}
