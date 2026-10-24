param([string]$UnityData = 'D:\unity hub\2022.3.62f3c1\Editor\Data')
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $engineReferences = Get-ChildItem -LiteralPath (Join-Path $UnityData 'Managed\UnityEngine') -Filter '*.dll' | ForEach-Object { '/reference:' + $_.FullName }
    $standardReferences = Get-ChildItem -LiteralPath (Join-Path $UnityData 'NetStandard\ref\2.1.0') -Filter '*.dll' | ForEach-Object { '/reference:' + $_.FullName }
    $mono = Join-Path $UnityData 'MonoBleedingEdge\bin\mono.exe'
    $compiler = Join-Path $UnityData 'MonoBleedingEdge\lib\mono\4.5\csc.exe'
    & $mono $compiler /noconfig /nologo /nostdlib /define:UNITY_EDITOR /target:library /out:Verification/DemoCompile.dll $standardReferences $engineReferences Assets/GlobalTransformDemo.cs Assets/LatticeView.cs
    if ($LASTEXITCODE -ne 0) { throw 'Editor compile failed.' }
    & $mono $compiler /noconfig /nologo /nostdlib /target:exe /out:Verification/SmokeCheck.exe $standardReferences $engineReferences Assets/GlobalTransformDemo.cs Assets/LatticeView.cs Verification/SmokeCheck.cs
    if ($LASTEXITCODE -ne 0) { throw 'Player compile failed.' }
    $previousMonoPath = $env:MONO_PATH
    try {
        $env:MONO_PATH = Join-Path $UnityData 'Managed\UnityEngine'
        & $mono Verification/SmokeCheck.exe
        if ($LASTEXITCODE -ne 0) { throw 'Algorithm smoke check failed.' }
    } finally { $env:MONO_PATH = $previousMonoPath }
} finally { Pop-Location }
