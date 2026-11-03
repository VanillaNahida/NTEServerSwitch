<div align="center">

# NTEServerSwitch 异环切服器

_✨ 异环官服B服切换器 ✨_

![NTEServerSwitch](./安魂曲.png)

[![License](https://img.shields.io/badge/License-GPL-green.svg)](https://opensource.org/license/gpl-3.0) [![C#](https://img.shields.io/badge/C%23-10%2B-blue.svg)](https://learn.microsoft.com/en-us/dotnet/csharp/) [![NTEServerSwitch](https://img.shields.io/badge/NTEServerSwitch-Project-orange.svg)](https://github.com/VanillaNahida/NTEServerSwitch) [![GitHub](https://img.shields.io/badge/作者-VanillaNahida-blue)](https://github.com/Zhalslar)

</div>

## 简介

《异环》官服与B服一键切换工具，帮助玩家快速在官服和B服之间切换，无需手动修改配置文件。

## 功能特性

- **自动检测**：自动从注册表检测游戏安装路径
- **一键切换**：点击按钮即可在官服与B服之间切换
- **不再膨胀**：切换时按官方清单把渠道差异文件整体替换，并删除另一个渠道独有的文件，反复切换体积不会越滚越大
- **资源缓存**：首次启动时从官方 CDN 拉取两个渠道的差异文件，存放在程序目录的 `sdkres` 里，之后切服只做本地复制
- **自动更新**：每次启动比对线上清单版本，版本变化时自动补下载
- **版本检查**：启动时顺带比对 GitHub 上的最新发行版，有新版本会直接问你要不要去下载（检查失败静默处理，不影响使用）
- **下载进度**：显示当前下载的文件、整体进度与实时速度（MB/s）
- **状态显示**：实时显示当前服务器状态，不同服务器显示不同颜色
- **进程检测**：切换前自动检测并提示关闭游戏相关进程
- **路径选择**：支持手动选择游戏安装路径
- **独立日志窗口**：不干扰主界面，可导出日志文件用于反馈

## 使用方法

1. 运行程序，系统会自动检测游戏安装路径
2. 若未自动检测到，请点击「选择」按钮手动选择游戏安装目录
3. 首次运行会自动建立资源缓存（官服约 55 MB、B服约 241 MB，受网速影响需要几分钟）
4. 点击「一键切官服」或「一键切B服」进行服务器切换
5. 需要看细节或反馈问题时，点「日志」打开独立日志窗口，可导出日志文件
6. 想手动看有没有新版本，点「检查更新」即可（启动时也会自动检查一次）

## 为什么以前越切越大

启动器的更新逻辑是「只覆盖、从不删除」：它只会下载目标渠道清单里的文件，不会清理另一个渠道留下的文件。
因此每切一次 B 服，就会多出一整套 `channelSdk/bilibili/**`（71 个文件、约 186 MB），切回官服后永久残留。

本工具的做法是把两个渠道的差异文件都缓存到本地，切服时：

1. 关闭游戏相关进程
2. 删除「目标渠道清单里没有、但本地存在」的差异文件（`UserData` 目录永远不动）
3. 用缓存里的文件覆盖过去（大小与 MD5 一致则跳过）
4. 最后写入 `Config.ini`

这样官服与 B服 之间反复切换，目录体积只会在这两个渠道的差异集之间来回变化，不会再累积。

## 技术原理

### 配置差异

程序通过改写游戏安装目录下的 `NTELauncher/Config/Config.ini` 来切换渠道：

- **官服**：`Branch=publish_ob`，`serialNumber=1`，无 `Partner`
- **B服**：`Branch=publish_ob_B`，`serialNumber=2`，`Partner=123`

### 清单来源

所有文件清单都来自官方 CDN，地址从 `Config.ini` 里的 `VersionInfoFileURL` 推导：

- 官服：`https://yhcdn1.wmupd.com/hd/publish_ob/launcher/Version.ini`
- B服：`https://yhcdn1.wmupd.com/hd/publish_ob_B/launcher/Version.ini`

Version.ini 里的 `FileListURL` 指向本次版本的 `AllFiles.xml`，单个文件下载地址是「AllFiles.xml 所在目录 + 文件路径 + .zip」。
所有下载内容都会用清单里的 MD5 与大小双重校验。

### 缓存目录

程序目录下会生成：

```
sdkres/
  official/files/...   官服差异文件（12 个，约 55 MB）
  bilibili/files/...   B服差异文件（82 个，约 241 MB）
  state.json           清单版本与文件校验信息
logs/                  日志文件
```

`sdkres` 可以随时删除，下次启动会重新建立（会优先从游戏目录里已有的文件就地复用，尽量少下载）。

### 降级策略

宁可少做事，也不能比只改配置更差：

- 网络不可用 / 清单拉取失败：只改 `Config.ini`，与旧版行为一致
- 本地版本与线上清单版本不一致：提示先让启动器更新一次，本次只改 `Config.ini`
- 个别文件下载失败：只跳过这些文件，其余照常

## 开发环境

- .NET 8.0
- WPF (Windows Presentation Foundation)
- Visual Studio 2022+

## 构建运行

```bash
# 克隆项目
git clone https://github.com/VanillaNahida/NTEServerSwitch.git

# 进入项目目录
cd NTEServerSwitch

# 构建项目
dotnet build

# 运行项目
dotnet run
```

## 发布

需要同时产出两个包（脚本已写好，直接运行即可）：

```powershell
pwsh -File ./publish.ps1
```

产物在 `dist/`：

| 包 | 是否需要装运行时 | 大小 |
| --- | --- | --- |
| `NTEServerSwitch_v<版本号>_need-runtime.zip` | 需要安装 .NET 8 桌面运行时 | 约 0.7 MB |
| `NTEServerSwitch_v<版本号>_self-contained.zip` | 免安装，解压即用 | 约 64 MB |

压缩包文件名带版本号（如 `NTEServerSwitch_v1.2.1_self-contained.zip`），默认读取 csproj 里的 `Version`，也可以用 `./publish.ps1 -Version 1.2.1` 手动指定。

主程序文件名是 `NTEServerSwitch.exe`（ASCII）。程序集名必须是纯 ASCII —— 中文 exe/dll 名在某些系统区域设置下会出现莫名其妙的加载问题，所以界面和日志保持中文，只有文件名用英文。

自包含版是 6 个文件（主程序 + 5 个 WPF 原生 DLL），这些原生 DLL 无法打进单文件，所以必须按压缩包分发。

### 自动发布（GitHub Actions）

仓库里已经配好两条工作流，都在 `.github/workflows/`：

| 工作流 | 触发方式 | 做什么 |
| --- | --- | --- |
| `release.yml` | 推送 `v*.*.*` 的 tag，或在 Actions 页面手动填版本号 | 构建两个包 → 生成更新说明 → 创建/更新 GitHub Release 并上传两个 zip |
| `build-windows.yml` | 手动触发，以及向 main 分支 push / PR | 只构建并上传 artifact，用于验证代码能不能编过，不发布 |

发版流程：先把 `NTEServerSwitch.csproj` 里的 `Version` 改成新版本号并提交，然后打 tag 推送即可：

```bash
git tag v1.2.0
git push origin v1.2.0
```

Release 的更新说明由 `.github/scripts/release-notes.ps1` 自动生成：按 Conventional Commits（`feat:` / `fix:` / `perf:` 等）分组列出本次提交，并附上两个安装包的直链下载表格、文件大小和使用步骤。

> 只写纯 PowerShell，不需要 Python。本地想预览更新说明：
> 
> ```powershell
> pwsh -File .github/scripts/release-notes.ps1 -Repository VanillaNahida/NTEServerSwitch -Tag v1.2.0 -Version 1.2.0 -FromTag v1.0.0 -DistDir dist -OutFile release-notes.md
> ```

## 项目结构

```
App.xaml(.cs)              程序入口、全局异常处理
src/
  MainWindow.xaml(.cs)     主界面
  LogWindow.xaml(.cs)      独立日志窗口
  AboutWindow.xaml(.cs)    关于
  Core/
    ServerChannel.cs       渠道定义（分支名、serialNumber、配色）
    Endpoints.cs           从 Config.ini 推导两个渠道的 CDN 地址
    Manifest.cs            Version.ini / AllFiles.xml 解析与差异集计算
    ResourceCache.cs       sdkres 读写与 state.json
    UpdateClient.cs        HTTP 拉取、重试、下载
    ResourceSyncer.cs      检查更新 → 重算差异 → 下载进缓存
    SwitchService.cs       切服：删多余 → 覆盖 → 改配置
    IniFile.cs             精准改写 Config.ini（保留注释与行尾）
    TextFile.cs            文本读写（保留编码与换行风格）
    HashUtil.cs            MD5 工具
    PathUtil.cs            清单路径安全拼接
    ProcessGuard.cs        关闭游戏相关进程
    LogService.cs          日志
    DownloadProgress.cs    下载进度（已下载字节数 + 实时速度）
    UpdateChecker.cs       检查 GitHub 上的最新发行版
publish.ps1                双包发布脚本（自包含 / 依赖运行时）
.github/workflows/         CI：build-windows.yml 构建、release.yml 自动发布
.github/scripts/           生成 Release 更新说明的 PowerShell 脚本
```

## 自测

### 冒烟自检（`--smoke-test`）

`--smoke-test` 只在 **Debug** 构建里存在：启动后自动打开主窗口和日志窗口、强制布局一次再退出，退出码 0 表示一切正常。用来验证**打出来的产物**能不能真正开窗（`InvariantGlobalization=true` 导致 WPF 字体缓存崩溃这个问题，就是靠它当场抓到的）。

正式发布会用 `-c Release` 编译，所以 Release 构建和发布出来的 exe **没有**这个参数，传了也会被当作普通启动忽略。本地要自检请显式用 Debug 构建：

```powershell
dotnet build NTEServerSwitch.csproj -c Debug
.\bin\Debug\net8.0-windows\NTEServerSwitch.exe --smoke-test
```

## 👥 贡献指南

- 🌟 Star 这个项目！
- 🐛 提交 Issue 报告问题
- 💡 提出新功能建议
- 🔧 提交 Pull Request 改进代码

## 注意事项

- 切换服务器前请确保游戏已完全关闭
- 本工具仅用于切换游戏服务器，不修改任何游戏数据
- `UserData` 目录（账号、设置）永远不动，不会被删除

# bug反馈
如果在使用过程中遇到任何问题，请通过以下方式反馈：
- [Issue](https://github.com/VanillaNahida/NTEServerSwitch/issues)
- QQ群：[195260107](https://qm.qq.com/q/1od5TMYrKE)

# QQ群：
 - 一群：621457510
 - 二群：1031065631
 - 三群：195260107 （推荐）
 - 四群：1074471035 （闲聊群）

# Star History

[![Star History Chart](https://api.star-history.com/svg?repos=VanillaNahida/NTEServerSwitch&type=Date)](https://star-history.com/#VanillaNahida/NTEServerSwitch&Date)
