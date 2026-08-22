# 温暖如初 · Warm As Before

一款基于 MAUI 的交互式角色扮演游戏，支持 AI 对话、立绘系统、地图探索、桌宠模式等功能。

## 功能特性

- **AI 智能对话** - 支持 OpenAI 兼容 API，离线时自动生成友好回复
- **立绘系统** - 随机选择服装和表情，支持多角色、位置动画
- **地图探索** - 场景移动、距离计算、防 teleport 机制
- **主题切换** - 经典/樱花粉/翠竹绿/晨雾蓝灰四种主题
- **桌宠模式** - 最小化窗口显示角色立绘
- **插件系统** - 支持 stdin/stdout 与主线程交互
- **存档系统** - 自动存档、手动存档、导入导出

## 系统要求

- Windows 10 (19041+) / Android 21+ / iOS 15+
- .NET 9 SDK
- 开放 AI API 密钥（可选）

## 构建

```bash
dotnet build -c Release -p:TargetFramework=net9.0-windows10.0.19041.0
```

## 许可证

MIT License (非商用)

## 作者

温暖如初项目组

## 赞助支持

如果您喜欢这个项目，欢迎赞助作者：
- [爱发电](https://ifdian.net/a/jqyhxkxt1145141026)
- [B站主页](https://space.bilibili.com/3546745275419060)
