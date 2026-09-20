# 应用与网站限制

打开左侧“使用限制”。启用服务后，新增应用程序或网站域名规则，设置一个或多个条件并保存：

- **正在专注时禁止使用**：专注开始后生效；暂停、结束或完成专注后解除此条件。
- **每天指定时间段**：采用本机时间，包含开始时间、不包含结束时间；支持跨午夜，起止相同表示全天。
- **每日可用分钟数**：累计前台使用时间，达到额度后限制；0 表示不设额度。按本地日期重置。

多个条件采用“任一满足即限制”。选中已有规则可修改、停用或删除。点击“暂停所有限制 15 分钟”可临时解除所有规则。

## 应用程序

“读取已安装的应用”读取 Windows 应用注册信息和当前用户的应用包；输入名称搜索并选择。便携程序或未提供可执行文件信息的安装项可手动填写进程名，例如 `game.exe`。

Equora 每秒检查前台窗口，命中规则时将窗口最小化；不关闭进程、不丢弃未保存内容。应用后台运行不计时，鼠标和键盘空闲超过 60 秒后暂停统计。Windows 设置、任务管理器、文件管理器和 Equora 等恢复工具不支持限制。

这是自主使用管理功能，不是系统级防绕过锁定：后台音频、服务和管理员程序不保证被阻止。请保持 Equora 运行。

## 网站（Edge / Chrome）

1. 点击“打开扩展目录”。完整构建的应用目录包含 `BrowserExtension` 和 `BrowserHost`。
2. 打开 `edge://extensions` 或 `chrome://extensions`，启用开发者模式，选择“加载解压缩的扩展”，选择 `BrowserExtension` 目录。
3. 复制扩展的 32 位 ID，粘贴到 Equora 的“扩展 ID”，点击“连接 Edge 与 Chrome”。
4. 在浏览器扩展管理页重新加载扩展，Equora 应显示“浏览器已连接”。两个浏览器的扩展 ID 不同时，可分别连接，先前 ID 会保留。

连接按钮只注册当前用户的本地浏览器消息组件，无需管理员权限。移动应用文件夹后需重新连接。开发构建需要 x64 .NET 10 运行时。

填写 `example.com` 同时限制其子域名，不影响 `notexample.com`。按当前获得焦点的浏览器窗口中的活动网页统计，每约 5 秒更新一次，不读取网页正文或保存 URL 路径。后台标签不计时，空闲超过 60 秒暂停计时。达到条件后，新打开与已经打开的匹配网页均转到限制页；限制页可临时允许该域名 5 分钟，然后返回网站首页。

网站规则要求扩展保持启用；隐私窗口需在浏览器中单独允许扩展运行。浏览器本身的内部页面不受限制。桌面端退出或心跳超过 12 秒未更新后，下一次检查解除限制；浏览器连接组件断开时撤销拦截。此版本没有执行浏览器商店发布。

## 任务分类与专注

任务页的“项目 · 管理”和“标签 · 管理”可新增、修改名称和颜色、删除分类。删除分类保留任务。任务详情可选择项目，并分配或取消标签。

专注主按钮随状态显示“开始”“暂停”“继续”。暂停时计时冻结，切换页面后仍保持，恢复后继续累计有效专注时间。

## 本地验证

```powershell
dotnet build desktop/Equora.App/Equora.App.csproj --no-restore -p:Platform=x64 -m:1 -nr:false
dotnet test desktop/Equora.App.Tests/Equora.App.Tests.csproj --no-restore -p:Platform=x64 -m:1 -nr:false --filter 'FullyQualifiedName!~StartupRegistrationTests'
node --test extension/tests/policy.test.cjs
python desktop/tools/test-browser-host.py
powershell -NoProfile -ExecutionPolicy Bypass -File desktop/tools/ui-review.ps1
```

浏览器扩展测试使用模拟浏览器 API；消息组件测试运行真实构建的 Host 并使用隔离规则文件。真实浏览器中的安装、权限授予和拦截页展示需要按上面的步骤连接验证。
