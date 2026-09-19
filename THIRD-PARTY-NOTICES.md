# Equora 第三方组件声明

本项目使用以下第三方开源组件。感谢所有贡献者。

## 原生核心(C++)

| 组件 | 版本 | 许可证 | 用途 | 许可证原文 |
|---|---|---|---|---|
| SQLite | 3.53.4 | Public Domain | 本地数据库引擎 | [sqlite.org/copyright.html](https://www.sqlite.org/copyright.html) |
| nlohmann-json | (vcpkg 清单) | MIT | JSON 序列化/反序列化 | [GitHub](https://github.com/nlohmann/json/blob/develop/LICENSE.MIT) |
| GoogleTest | 1.18.0 | BSD-3-Clause | C++ 单元测试框架 | [GitHub](https://github.com/google/googletest/blob/main/LICENSE) |

## 桌面应用(C#)

| 组件 | 版本 | 许可证 | 用途 | 许可证原文 |
|---|---|---|---|---|
| WinUI 3 / Windows App SDK | 1.8.x | MIT | 用户界面框架 | [GitHub](https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE) |
| CommunityToolkit.Mvvm | 8.4.x | MIT | MVVM 辅助库 | [GitHub](https://github.com/CommunityToolkit/dotnet/blob/main/License.md) |
| Microsoft.NET.Test.Sdk | 17.x | MIT | 测试 SDK | [NuGet](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk) |
| xUnit | 2.x | Apache-2.0 | C# 测试框架 | [GitHub](https://github.com/xunit/xunit/blob/main/LICENSE) |

## 同步服务器(可选,C++)

| 组件 | 版本 | 许可证 | 用途 | 许可证原文 |
|---|---|---|---|---|
| Drogon | (vcpkg 清单) | MIT | HTTP 服务器框架 | [GitHub](https://github.com/drogonframework/drogon/blob/master/LICENSE) |
| PostgreSQL | 16 | PostgreSQL License | 服务器数据库 | [postgresql.org](https://www.postgresql.org/about/licence/) |
| libpq | (vcpkg 清单) PostgreSQL License | PostgreSQL C 客户端库 | [postgresql.org](https://www.postgresql.org/about/licence/) |

## 构建工具

| 工具 | 许可证 | 用途 |
|---|---|---|
| CMake | BSD-3-Clause | 构建系统 |
| vcpkg | MIT | C++ 依赖管理 |
| Visual Studio 2026 | (商业许可) | 编译器与 IDE |
| .NET SDK 10 | MIT | C# 编译与运行时 |

## 声明

- 本项目不修改任何第三方组件源码(仅通过官方接口使用)。
- 所有第三方组件的版权与许可归属其原作者。
- 完整许可文本请访问各组件的许可证原文链接。
- 如有遗漏,请通过 [GitHub Issues](https://github.com/Furina-1314/Equora/issues) 报告。
