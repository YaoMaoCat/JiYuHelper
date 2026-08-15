# JiYuHelper

极域电子教室软件安全研究工具 / Security research toolkit for JiYu electronic classroom software

> **⚠️ 免责声明 / Disclaimer**
>
> 本项目**仅用于网络安全研究与教育目的**，仅允许在您拥有明确授权的环境中使用。
> 严禁用于任何未经授权的攻击、入侵或破坏行为。使用本项目产生的任何后果由使用者自行承担。
>
> This project is **for security research and education ONLY**. Use it exclusively in
> environments you are explicitly authorized to test. Any unauthorized use is strictly
> prohibited. The author assumes no liability for any consequence of misuse.

---

## 简介 / Overview

JiYuHelper 是针对极域课堂管理系统（教师端/学生端）的协议分析与安全研究工具，包含：

- **教师机发现**：UDP 组播监听（OONC/CANC）与网段 IP 扫描（TCP 4806 + WORB 握手验证）
- **Hook 注入与控制**：向本机极域进程（StudentMain.exe / MasterHelper.exe）注入 Hook DLL，实现远程控制拦截、窗口化、屏幕伪造等
- **协议漏洞研究**：UPFB 上传通道任意文件写利用、协议崩溃测试等（详见「漏洞利用」页）
- **新手/开发者双模式**：新手模式显示通俗说明，开发者模式显示技术细节

A security research toolkit for JiYu classroom management suite (teacher/student side), including:

- **Teacher discovery**: UDP multicast listening (OONC/CANC) and subnet IP scanning (TCP 4806 + WORB handshake verification)
- **Hook injection & control**: inject hook DLLs into local JiYu processes (StudentMain.exe / MasterHelper.exe) for remote-control blocking, windowing, screen spoofing, etc.
- **Protocol vulnerability research**: UPFB upload-channel arbitrary file write, protocol crash testing (see the "Vulnerabilities" page)
- **Novice/Developer dual modes**: plain-language descriptions for novices, technical details for developers

---

## 功能特性 / Features

| 模块 | 说明 / Description |
|---|---|
| 扫描 Discover | 组播 / 网段两种方式发现教师机 |
| 控制 Hook | 14 个注入功能开关：远程输入拦截、进程守护、屏幕假屏、键盘钩子绕过等，支持自动重注入与热更新 |
| 攻击 Attack | FILESUBMIT / ANSWERSHEET / UPFB 等协议测试 |
| 漏洞 Vulnerability | UPFB 任意文件写入利用（任意路径、任意偏移、≤64KB） |
| 日志 Log | 全局日志：注入状态、拦截事件、攻击统计 |
| 设置 Settings | 主题（跟随系统/蓝色/明亮/深色）、窗口标题、界面模式、假屏图管理、配置导入导出 |

### Hook 功能列表 / Hook feature list

- **远程控制拦截（脱控）**：远程输入拦截、输入锁定放行、进程操作守护、进程终止能力屏蔽、设备过滤屏蔽（USB/CD/程序）、网络仿真屏蔽
- **界面与进程（窗口化）**：置顶窗口剥离、焦点锁定拦截、应用列表屏蔽、进程列表屏蔽
- **屏幕监控**：屏幕假屏（注入伪造画面）、屏幕捕获屏蔽（BitBlt 假画面）、黑屏监控（自动窗口化）
- **输入**：键盘钩子绕过（低级键鼠钩子替换为空钩子）

---

## 架构 / Architecture

```
JiYuHelper (WinUI 3 / .NET 8, x64)  —— 主程序: 发现 / 控制 / 攻击 / 漏洞利用 / 日志
    │
    ├── Core/        业务逻辑 (发现、注入、协议包构造、攻击引擎、设置)
    ├── Models/      数据模型
    ├── Views/       UI 页面 (扫描 / 控制 / 攻击 / 漏洞 / 日志 / 设置 / 帮助)
    │
    └── Native/      C++17 / CMake 原生 Hook DLL (x86, 注入 32 位极域进程)
         ├── jyhelper_main.dll     -> 注入 StudentMain.exe
         ├── jyhelper_master.dll   -> 注入 MasterHelper.exe
         └── 基于 MinHook, 通过命名管道与主程序通信 (JYHookHelper / JYMasterHelper)
```

- 极域软件为 32 位，注入 DLL 必须为 **x86** 构建；主程序为 **x64**
- 注入方式：x64 主程序 → WoW64 `NtCreateThreadEx` + 32 位 `LoadLibraryW`
- DLL 与主程序通过命名管道通信：拦截事件回传（`HOOK` / `BLOCKED` / `HEARTBEAT` …），主程序下发功能掩码热更新（`UPDATE|0x…`）

---

## 构建 / Build

### 主程序 (x64)

```bash
dotnet build JiYuHelper.csproj -p:Platform=x64 -c Debug
```

环境要求：.NET 8 SDK、Windows 10 19041+ SDK、Windows App SDK 2.3.1（NuGet 自动还原）。

### 原生 Hook DLL (x86)

```bash
cd Native
# 需要 Visual Studio 的 vcvars32.bat 环境
cmake --preset x86-release
cmake --build out/build/x86-release
```

产物：`jyhelper_main.dll` / `jyhelper_master.dll`，复制到主程序 exe 同目录即可。

---

## 使用 / Usage

1. **扫描**：发现并选中教师机
2. **控制**：勾选 Hook 功能 → 「注入并启用」（注入 MasterHelper 需管理员）
3. **攻击 / 漏洞**：按需进行协议测试与漏洞利用
4. **日志**：查看实时状态

新手可在设置页切换到「新手模式」获取通俗化的界面说明。

---

## 安全研究说明 / Security Research Notes

- 分析对象：极域课堂管理系统软件 v6.0 2021 豪华版（CMPC 2.07.0.17364）
- 相关分析文档与工具链（Binary Ninja 数据库、崩溃检测探针等）仅保留在研究环境，未随仓库分发
- 发现的问题请及时向厂商反馈修复

---

## 许可 / License

本项目仅用于安全研究，未附带开源许可证（All rights reserved）。
