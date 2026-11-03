# 一键打包：同时产出自包含版（免装运行时）和框架依赖版（1 MB，需装 .NET 8 桌面运行时）
param(
    # 压缩包命名用的版本号（如 1.2.1），留空则读取 csproj 里的 Version
    [string]$Version = ''
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$dist = Join-Path $PSScriptRoot 'dist'
if (Test-Path $dist) {
    try {
        Remove-Item $dist -Recurse -Force -ErrorAction Stop
    }
    catch {
        # dist 里的 exe/dll 正被运行中的切服器占用。Move-Item 对目录只会搬走能搬的部分，
        # 所以这里一律以磁盘实际结果为准，绝不能让一次占用把整次打包搞失败。
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $stale = Join-Path $PSScriptRoot ('dist.old-' + $stamp)
        try { Move-Item $dist $stale -ErrorAction Stop } catch { }

        if (Test-Path $dist) {
            # 还剩一个目录壳：空壳就删掉继续用 dist，里面有残留文件才换新目录
            $left = @(Get-ChildItem $dist -Force -ErrorAction SilentlyContinue)
            if ($left.Count -eq 0) { Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue }
        }

        if (Test-Path $dist) {
            if (Test-Path $stale) { Remove-Item $stale -Recurse -Force -ErrorAction SilentlyContinue }
            $dist = Join-Path $PSScriptRoot ('dist-' + $stamp)
            Write-Host "==> dist 正被运行中的切服器占用，本次产物改放到 $(Split-Path $dist -Leaf)" -ForegroundColor Yellow
            Write-Host '    关掉切服器后重跑本脚本即可回到 dist' -ForegroundColor Yellow
        }
        else {
            Write-Host "==> dist 被占用，旧产物已重命名为 $(Split-Path $stale -Leaf)（确认没在用后可手动删除）" -ForegroundColor Yellow
        }
    }
}
New-Item -ItemType Directory -Path $dist -Force | Out-Null

if (-not $Version.Trim()) {
    $Version = ([xml](Get-Content (Join-Path $PSScriptRoot 'NTEServerSwitch.csproj') -Raw)).Project.PropertyGroup.Version.Trim()
}
$Version = $Version.TrimStart('v', 'V')
if ($Version -notmatch '^[0-9]+(\.[0-9]+)+$') {
    throw "版本号格式不对：$Version（应形如 1.2.1）"
}

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
    $zip = Join-Path $dist ("NTEServerSwitch_v" + $Version + "_" + $dir.Name + ".zip")
    Write-Host "==> 压缩 $($dir.Name)" -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $dir.FullName '*') -DestinationPath $zip -Force
}

Write-Host ''
Write-Host ('完成，产物在 ' + (Split-Path $dist -Leaf) + '\：') -ForegroundColor Green
Get-ChildItem $dist -Filter *.zip | ForEach-Object {
    Write-Host ('  {0}  ({1:N1} MB)' -f $_.Name, ($_.Length / 1MB))
}
