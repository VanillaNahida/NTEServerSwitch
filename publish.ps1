# 一键打包：同时产出自包含版（免装运行时）和框架依赖版（1 MB，需装 .NET 8 桌面运行时）
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$dist = Join-Path $PSScriptRoot 'dist'
if (Test-Path $dist) {
    try {
        Remove-Item $dist -Recurse -Force -ErrorAction Stop
    }
    catch {
        # dist 里的 exe/dll 可能正被运行中的切服器占用；先试改名让开，改不动就换个输出目录
        $moved = $false
        $stale = Join-Path $PSScriptRoot ('dist.old-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        try {
            Move-Item $dist $stale -ErrorAction Stop
            $moved = $true
            Write-Host "==> dist 被占用，已重命名为 $(Split-Path $stale -Leaf)（确认没在用后可手动删除）" -ForegroundColor Yellow
        }
        catch { }
        if (-not $moved) {
            $dist = Join-Path $PSScriptRoot ('dist-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
            Write-Host "==> dist 正被运行中的切服器占用，本次产物改放到 $(Split-Path $dist -Leaf)" -ForegroundColor Yellow
            Write-Host "    关掉切服器后重跑本脚本即可回到 dist" -ForegroundColor Yellow
        }
    }
}
New-Item -ItemType Directory -Path $dist -Force | Out-Null

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
        # 注意：不要加 -p:InvariantGlobalization=true，WPF 会因为缺 ICU 在排版时抛 TypeInitializationException
        $cmd += @('-p:EnableCompressionInSingleFile=true', '-p:InvariantGlobalization=false')
    }

    Write-Host "==> 发布 $Name" -ForegroundColor Cyan
    & dotnet @cmd
    if ($LASTEXITCODE -ne 0) { throw "发布失败：$Name" }
}

Publish-Variant -Name 'self-contained' -SelfContained $true
Publish-Variant -Name 'need-runtime' -SelfContained $false

foreach ($dir in Get-ChildItem $dist -Directory) {
    $zip = Join-Path $dist ("NTEServerSwitch_" + $dir.Name + ".zip")
    Write-Host "==> 压缩 $($dir.Name)" -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $dir.FullName '*') -DestinationPath $zip -Force
}

Write-Host ''
Write-Host '完成，产物在 dist\\：' -ForegroundColor Green
Get-ChildItem $dist -Filter *.zip | ForEach-Object {
    Write-Host ('  {0}  ({1:N1} MB)' -f $_.Name, ($_.Length / 1MB))
}
