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
- **状态显示**：实时显示当前服务器状态，不同服务器显示不同颜色
- **进程检测**：切换前自动检测并提示关闭游戏相关进程
- **路径选择**：支持手动选择游戏安装路径

## 使用方法

1. 运行程序，系统会自动检测游戏安装路径
2. 若未自动检测到，请点击「选择」按钮手动选择游戏安装目录
3. 点击「一键切官服」或「一键切B服」进行服务器切换
4. 切换成功后会弹出提示窗口

## 技术原理

程序通过修改游戏安装目录下的 `NTELauncher/Config/Config.ini` 文件来实现服务器切换：

- **官服**：`Branch=publish_ob`，`serialNumber=1`
- **B服**：`Branch=publish_ob_B`，`serialNumber=2`，`Partner=123`

## 开发环境

- .NET 8.0
- WPF (Windows Presentation Foundation)
- Visual Studio 2022+

## 构建运行

```bash
# 克隆项目
git clone https://github.com/your-repo/NTEServerSwitch.git

# 进入项目目录
cd NTEServerSwitch

# 构建项目
dotnet build

# 运行项目
dotnet run
```

## 👥 贡献指南

- 🌟 Star 这个项目！
- 🐛 提交 Issue 报告问题
- 💡 提出新功能建议
- 🔧 提交 Pull Request 改进代码

## 注意事项

- 切换服务器前请确保游戏已完全关闭
- 本工具仅用于切换游戏服务器，不修改任何游戏数据。

# bug反馈
如果在使用过程中遇到任何问题，请通过以下方式反馈：
- [Issue](https://github.com/VanillaNahida/Group-Geetest-Verify/issues)
- QQ群：[195260107](https://qm.qq.com/q/1od5TMYrKE)

# QQ群：
 - 一群：621457510
 - 二群：1031065631
 - 三群：195260107 （推荐）
 - 四群：1074471035 （闲聊群）

# Star History

[![Star History Chart](https://api.star-history.com/svg?repos=VanillaNahida/NTEServerSwitch&type=Date)](https://star-history.com/#VanillaNahida/NTEServerSwitch&Date)
