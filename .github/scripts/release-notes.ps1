<#
.SYNOPSIS
    根据 Conventional Commits 生成 GitHub Release 说明，并在末尾附上下载表格。
.DESCRIPTION
    用法（在仓库根目录执行）：
      pwsh -File .github/scripts/release-notes.ps1 -Repository owner/repo -Tag v1.2.0 -Version 1.2.0 -FromTag v1.1.0 -DistDir dist -OutFile release-notes.md
    -FromTag 为空时（仓库还没有旧 tag）只统计最近的提交，并跳过『完整变更』那一行。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Repository,
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$FromTag = '',
    [string]$DistDir = '',
    [Parameter(Mandatory = $true)][string]$OutFile
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Invoke-Git {
    param([string[]]$GitArgs)
    $output = & git @GitArgs 2>$null
    if ($LASTEXITCODE -ne 0) { return @() }
    return @($output)
}

# ---------- 收集提交 ----------
$range = ''
if ($FromTag) {
    $existing = Invoke-Git @('rev-parse', '--verify', '--quiet', ($FromTag + '^{commit}'))
    if ($existing.Count -gt 0) { $range = $FromTag + '..HEAD' }
    else { Write-Host ('警告：找不到基准 tag ' + $FromTag + '，改为只统计最近提交') -ForegroundColor Yellow }
}

if ($range) {
    $raw = Invoke-Git @('log', '--no-merges', '--pretty=format:%h%x09%s', $range)
} else {
    $raw = Invoke-Git @('log', '--no-merges', '--pretty=format:%h%x09%s', '-n', '50')
}

$groups = [ordered]@{
    'breaking' = @{ Title = '⚠️ 破坏性变更'; Items = New-Object System.Collections.ArrayList }
    'feat'     = @{ Title = '✨ 新功能';     Items = New-Object System.Collections.ArrayList }
    'fix'      = @{ Title = '🐛 问题修复';   Items = New-Object System.Collections.ArrayList }
    'perf'     = @{ Title = '⚡ 性能优化';   Items = New-Object System.Collections.ArrayList }
    'refactor' = @{ Title = '♻️ 重构';       Items = New-Object System.Collections.ArrayList }
    'style'    = @{ Title = '💄 样式调整';   Items = New-Object System.Collections.ArrayList }
    'docs'     = @{ Title = '📖 文档';       Items = New-Object System.Collections.ArrayList }
    'test'     = @{ Title = '✅ 测试';       Items = New-Object System.Collections.ArrayList }
    'build'    = @{ Title = '🔧 构建 / CI';  Items = New-Object System.Collections.ArrayList }
    'ci'       = @{ Title = '🔧 构建 / CI';  Items = New-Object System.Collections.ArrayList }
    'revert'   = @{ Title = '⏪ 回滚';       Items = New-Object System.Collections.ArrayList }
    'other'    = @{ Title = '🗂 其他改动';   Items = New-Object System.Collections.ArrayList }
}

$noise = New-Object System.Text.RegularExpressions.Regex('(更新|升级)?\s*版本号|bump(\s+the)?\s+version|^release\b', 'IgnoreCase')
$pattern = New-Object System.Text.RegularExpressions.Regex('^(?<type>[A-Za-z]+)(\((?<scope>[^)]*)\))?(?<bang>!)?:\s*(?<subject>.+)$')

$count = 0
foreach ($line in $raw) {
    if (-not $line) { continue }
    $parts = $line -split "`t", 2
    if ($parts.Count -lt 2) { continue }
    $sha = $parts[0].Trim()
    $subject = $parts[1].Trim()
    if (-not $subject) { continue }
    if ($noise.IsMatch($subject)) { continue }

    $match = $pattern.Match($subject)
    $type = 'other'
    $scope = ''
    $text = $subject
    $isBreaking = $false

    if ($match.Success) {
        $type = $match.Groups['type'].Value.ToLowerInvariant()
        if ($match.Groups['scope'].Success) { $scope = $match.Groups['scope'].Value.Trim() }
        if ($match.Groups['bang'].Success) { $isBreaking = $true }
        $text = $match.Groups['subject'].Value.Trim()
    }

    if (-not $groups.Contains($type)) { $type = 'other' }

    $link = '([' + $sha + '](https://github.com/' + $Repository + '/commit/' + $sha + '))'
    $scopeText = ''
    if ($scope) { $scopeText = '**' + $scope + '**：' }
    $item = '- ' + $scopeText + $text + ' ' + $link

    if ($isBreaking) {
        [void]$groups['breaking'].Items.Add($item)
    } else {
        [void]$groups[$type].Items.Add($item)
    }
    $count++
}

# ---------- 拼装说明 ----------
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('## 更新说明')
[void]$sb.AppendLine()

if ($count -eq 0) {
    [void]$sb.AppendLine('- 本版本主要是内部整理与构建调整。')
    [void]$sb.AppendLine()
}

foreach ($key in $groups.Keys) {
    $group = $groups[$key]
    if ($group.Items.Count -eq 0) { continue }
    [void]$sb.AppendLine('### ' + $group.Title)
    [void]$sb.AppendLine()
    foreach ($item in $group.Items) {
        [void]$sb.AppendLine($item)
    }
    [void]$sb.AppendLine()
}

if ($range) {
    [void]$sb.AppendLine('**完整变更**：[' + $FromTag + '...' + $Tag + '](https://github.com/' + $Repository + '/compare/' + $FromTag + '...' + $Tag + ')')
    [void]$sb.AppendLine()
}

# ---------- 下载表格 ----------
$assets = @(
    @{ Name = 'NTEServerSwitch_self-contained.zip'; Desc = '**推荐**。自带 .NET 运行时，解压即用，什么都不用装'; Hint = '约 65 MB' },
    @{ Name = 'NTEServerSwitch_need-runtime.zip'; Desc = '体积小，但需要先装 [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)'; Hint = '约 1 MB' }
)

function Get-AssetSize {
    param([string]$Name)
    if (-not $DistDir) { return '' }
    $path = Join-Path $DistDir $Name
    if (-not (Test-Path $path)) { return '' }
    $mb = (Get-Item $path).Length / 1MB
    if ($mb -lt 1) { return '约 ' + [string][math]::Round($mb, 1) + ' MB' }
    return '约 ' + [string][math]::Round($mb, 0) + ' MB'
}

[void]$sb.AppendLine('---')
[void]$sb.AppendLine()
[void]$sb.AppendLine('## 下载说明')
[void]$sb.AppendLine()
[void]$sb.AppendLine('> [!TIP]')
[void]$sb.AppendLine('> 不确定选哪个？绝大多数 Windows 10/11 用户下载 `NTEServerSwitch_self-contained.zip` 即可。')
[void]$sb.AppendLine()
[void]$sb.AppendLine('### 程序包说明')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| 文件 | 说明 | 大小 | 下载 |')
[void]$sb.AppendLine('| --- | --- | --- | --- |')
foreach ($asset in $assets) {
    $size = Get-AssetSize -Name $asset.Name
    if (-not $size) { $size = $asset.Hint }
    $url = 'https://github.com/' + $Repository + '/releases/download/' + $Tag + '/' + $asset.Name
    $download = '[点击下载](' + $url + ')'
    [void]$sb.AppendLine('| ' + $asset.Name + ' | ' + $asset.Desc + ' | ' + $size + ' | ' + $download + ' |')
}
[void]$sb.AppendLine()
[void]$sb.AppendLine('### 使用方法')
[void]$sb.AppendLine()
[void]$sb.AppendLine('1. 下载上面的压缩包并解压到任意目录（不要解压进游戏目录）。')
[void]$sb.AppendLine('2. 双击 `NTEServerSwitch.exe` 运行。程序需要管理员权限，会自己弹出 UAC 提示。')
[void]$sb.AppendLine('3. 首次运行会自动检测游戏目录、建立 `sdkres` 资源缓存，之后切换服务器只做本地文件替换，不会再重复下载。')
[void]$sb.AppendLine('4. 点『一键切官服』/『一键切B服』完成切换；切换前会自动关闭启动器与游戏进程。')
[void]$sb.AppendLine()
[void]$sb.AppendLine('> [!NOTE]')
[void]$sb.AppendLine('> 若杀毒软件报毒，多为单文件打包的常见误报，添加信任即可。本程序完全开源，代码就在本仓库。')
[void]$sb.AppendLine()
[void]$sb.AppendLine('<!-- generated by .github/scripts/release-notes.ps1 -->')

$parent = Split-Path -Parent $OutFile
if ($parent -and -not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
[System.IO.File]::WriteAllText($OutFile, $sb.ToString(), $utf8)

Write-Host ('已生成 ' + $OutFile + '（' + $count + ' 条提交）') -ForegroundColor Green
