# 一键打包：同时产出自包含版（免装运行时）和框架依赖版（1 MB，需装 .NET 8 桌面运行时）
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$dist = Join-Path $PSScriptRoot 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

function Publish-Variant {
    param([string]$Name, [bool]$SelfContained)

    $out = Join-Path $dist $Name
    $cmd = @(
        'publish', 'NTEServerSwitch.csproj',
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', $SelfContained.ToString().ToLowerInvariant(),
        '-p:PublishSingleFile=true',
        '-p:DebugType=none',
        '-o', $out,
        '-v', 'quiet',
        '--nologo'
    )
    if ($SelfContained) {
        $cmd += @('-p:EnableCompressionInSingleFile=true', '-p:InvariantGlobalization=true')
    }

    Write-Host "==> 发布 $Name" -ForegroundColor Cyan
    & dotnet @cmd
    if ($LASTEXITCODE -ne 0) { throw "发布失败：$Name" }
}

Publish-Variant -Name 'self-contained' -SelfContained $true
Publish-Variant -Name 'need-runtime' -SelfContained $false

foreach ($dir in Get-ChildItem $dist -Directory) {
    $zip = Join-Path $dist ("异环切服器_" + $dir.Name + ".zip")
    Write-Host "==> 压缩 $($dir.Name)" -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $dir.FullName '*') -DestinationPath $zip -Force
}

Write-Host ''
Write-Host '完成，产物在 dist\\：' -ForegroundColor Green
Get-ChildItem $dist -Filter *.zip | ForEach-Object {
    Write-Host ('  {0}  ({1:N1} MB)' -f $_.Name, ($_.Length / 1MB))
}
