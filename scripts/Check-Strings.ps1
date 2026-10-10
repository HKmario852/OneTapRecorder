# Checks the UI strings: both .resx files have the same keys, and every key used in code or XAML exists.
$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/OneTapRecorder'

function Get-Keys([string]$file) { ([xml](Get-Content -LiteralPath $file -Raw -Encoding UTF8)).root.data | ForEach-Object { $_.name } }
$english = @(Get-Keys (Join-Path $source 'Strings.resx'))
$chinese = @(Get-Keys (Join-Path $source 'Strings.zh-Hant.resx'))
$problems = @()
$problems += Compare-Object $english $chinese | ForEach-Object {
    if ($_.SideIndicator -eq '<=') { "Missing from Strings.zh-Hant.resx: $($_.InputObject)" } else { "Missing from Strings.resx: $($_.InputObject)" }
}

$used = New-Object 'System.Collections.Generic.HashSet[string]'
$code = Get-ChildItem -LiteralPath $source -Recurse -File -Include *.cs, *.xaml | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
foreach ($file in $code) {
    $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    # L("Key"), L(cond ? "A" : "B"), Localizer.Text("Key"), Localizer.Format("Key", ...)
    foreach ($m in [regex]::Matches($text, '(?:\bL|Localizer\.(?:Text|Format))\(\s*(?:[^;"()]*\?\s*)?"(\w+)"(?:\s*:\s*"(\w+)")?')) {
        foreach ($g in 1, 2) { if ($m.Groups[$g].Success) { [void]$used.Add($m.Groups[$g].Value) } }
    }
    foreach ($m in [regex]::Matches($text, '\{DynamicResource (\w+)\}')) { [void]$used.Add($m.Groups[1].Value) }
}
$xamlKeys = [regex]::Match((Get-Content -LiteralPath (Join-Path $source 'Localizer.cs') -Raw), 'XamlKeys\s*=\s*\[(.*?)\];', 'Singleline').Groups[1].Value
foreach ($m in [regex]::Matches($xamlKeys, '"(\w+)"')) { [void]$used.Add($m.Groups[1].Value) }

$problems += $used | Where-Object { $english -notcontains $_ } | Sort-Object | ForEach-Object { "Used but not defined: $_" }
$unused = $english | Where-Object { -not $used.Contains($_) }
if ($unused) { Write-Warning ("Defined but not used: " + ($unused -join ', ')) }
if ($problems) { $problems | ForEach-Object { Write-Host "::error::$_" }; exit 1 }
Write-Host "$($english.Count) strings in both languages; $($used.Count) keys used, all defined."
