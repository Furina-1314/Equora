# 衡序 Equora 浏览器扩展(Edge / Chrome,MV3)

专注会话期间,按桌面端下发的名单温和限制网站。**扩展不持久化限制规则**:
每次从 Native Host 拉取当前专注状态,用 `declarativeNetRequest` 的
**会话规则**下发 —— 浏览器重启、扩展崩溃、主程序异常后规则自然清空,
绝不可能把用户永久锁在网络之外。

## 隐私边界

- 只读取**域名**(不采集页面内容、不记录浏览历史)。
- 预算计时只按域名累计分钟数,存于扩展本地 `chrome.storage.local`。

## 开发安装(解包加载)

1. 构建并运行一次桌面端(生成数据库)。
2. 构建 Native Host:

   ```bash
   cd desktop
   dotnet build Equora.NativeHost -c Release
   ```

3. 生成 host manifest 并注册注册表:

   ```powershell
   cd desktop/Equora.NativeHost/bin/Release/net10.0-windows10.0.19041.0
   ./com.equora.nativehost.exe --print-manifest . > com.equora.nativehost.json
   # 用文本编辑器把 EXTENSION_ID_PLACEHOLDER 替换为第 5 步加载扩展后的真实 ID
   reg add HKCU\Software\Google\Chrome\NativeMessagingHosts\com.equora.nativehost /ve /t REG_SZ /d "<本目录>\com.equora.nativehost.json"
   # Edge 对应键:HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.equora.nativehost
   ```

4. 在 `chrome://extensions`(Edge 为 `edge://extensions`)开启开发者模式。
5. 「加载解包的扩展」选择本目录(`extension/Equora.BrowserExtension`)。
   首次图标缺失不影响功能;可放置 `icon128.png`。
6. 桌面端开启一次专注(默认预设可含 blocked_sites,如 `["weibo.com","bilibili.com"]`),
   访问受限域名应看到拦截页(返回任务 / 允许 5 分钟)。

## 消息协议(v1)

帧格式遵循 Chrome Native Messaging(4 字节小端长度 + UTF-8 JSON)。

| 方向 | type | 说明 |
|---|---|---|
| 扩展 → host | `hello` | 版本握手 |
| 扩展 → host | `query` | 拉取当前专注状态(每 30s) |
| host → 扩展 | `state` | `FocusGateState`(focusing/名单/预算) |
| 双向 | `error` | `version-mismatch` / `checksum-mismatch` / `replay` / … |

每条消息携带 `protocolVersion`、`nonce`(严格递增,重放拒绝)、
`checksum`(FNV-1a 防传输损坏;安全边界由 allowed_origins 保证)。

## 已知限制(当前阶段)

- 拦截重定向用 `regexSubstitution` 简化实现,复杂 URL 编码场景未覆盖,
  生产化前替换为 `redirect` + query 参数方案并补集成测试。
- 紧急解锁(绕过所有限制的系统级出口)在桌面端通知中心,P12 一并实现。
