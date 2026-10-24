# 第三方组件

本项目通过 NuGet 使用以下组件。具体直接/传递依赖版本及包内容哈希以 `packages.lock.json` 为准。

| 组件 | 版本 | 包声明的许可 | 项目 |
| --- | --- | --- | --- |
| Markdig | 0.37.0 | BSD-2-Clause | https://github.com/lunet-io/markdig |
| Microsoft.Data.Sqlite / Core | 10.0.13 | MIT | https://github.com/dotnet/efcore |
| SQLitePCLRaw | 2.1.13 | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |
| .NET / WPF 自包含运行时 | 随 SDK 发布解析 | 参见运行时附带 LICENSE / THIRD-PARTY-NOTICES | https://github.com/dotnet |

原设计引用 Microsoft.Data.Sqlite 10.0.0。安装时 NuGet 对它所带的旧 SQLite 库报告 NU1903，因此本实现采用 10.0.13，并固定解析后的依赖。发布目录保留运行时附带的许可文本。

许可全文随发布包的 licenses 目录提供；组件版权归各自作者所有。
