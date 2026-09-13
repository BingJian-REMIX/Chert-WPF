# Chert Launcher / 燧石启动器 — Minecraft 启动器 (WPF)

> **当前版本：v2.5.6（公测 / 公开测试）** · C# / WPF / .NET 10 · Windows

燧石启动器（Chert Launcher，原 MCLCS）是一个用 C# / WPF 实现的 Minecraft 启动器，覆盖版本安装、启动、崩溃修复、下载、Mod 管理与工具箱等。与 [MCLCS-Linux](https://cnb.cool/RLRS-Studio/MCLCS-Linux) 共享核心（`Chert.Core`），两端功能持续对齐。

## 下载

发布包提供两种形态：

| 形态 | 说明 | 依赖 |
| --- | --- | --- |
| **自包含（推荐）** | 解压即用、双击即跑 | 无，内嵌完整 .NET 10 运行时 |
| **轻量（Light）** | 体积小（约 1MB） | 需目标机已装 .NET 10 桌面运行时 |

[CNB Releases](https://cnb.cool/RLRS-Studio/Chert-WPF/-/releases)

下载后直接解压，运行 `Chert Launcher.exe` 即可。启动器内置自动更新器：启动时读取 GitHub Pages 上的 `latest.json`，发现新版本后直接下载 CNB Release 直链、解压并原地替换安装目录、接力启动新版本，全程无需手动下载。

## 链接

- 主仓库（GitHub）：<https://github.com/BingJian-REMIX/Chert-WPF>
- 更新信息源（GitHub Pages）：<https://remix-laser-raising-studio.github.io/Chert-upgrade/latest.json>

## 许可

以 **MIT 许可证** 开源发布。本项目与 Mojang / Microsoft 无关，非官方产品；分发物不包含 Minecraft 核心游戏文件（版本 jar / assets / libraries 由用户从官方源或其授权镜像按需下载，须持有正版账号）。
