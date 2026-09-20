param(
    [string]$Exe = (Join-Path $PSScriptRoot '../Equora.App/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Equora.App.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/ui-review')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase, System.Drawing, System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ReviewWindow {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hgt, bool redraw);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
}
'@
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$dataDirectory = Join-Path $OutputDirectory ('data-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dataDirectory | Out-Null
$previousData = $env:EQUORA_TEST_DATA_DIRECTORY
$env:EQUORA_TEST_DATA_DIRECTORY = $dataDirectory
try { $app = Start-Process -FilePath $Exe -ArgumentList '--data-directory', ('"' + [IO.Path]::GetFullPath($dataDirectory) + '"') -WindowStyle Normal -PassThru }
finally { $env:EQUORA_TEST_DATA_DIRECTORY = $previousData }
try {
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        Start-Sleep -Milliseconds 250
        $app.Refresh()
        if ($app.HasExited) { throw 'Application exited before a window was created.' }
        if ($app.MainWindowHandle -ne 0) { break }
    }
    $handle = $app.MainWindowHandle
    if ($handle -eq 0) { throw 'Window did not appear.' }
    [ReviewWindow]::MoveWindow($handle, 20, 20, 1440, 940, $true) | Out-Null
    [ReviewWindow]::SetForegroundWindow($handle) | Out-Null
    $script:ui = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    Start-Sleep -Milliseconds 700
    function Find-Ui([string]$name, [string]$id = '') {
        $property = if ($id) { [System.Windows.Automation.AutomationElement]::AutomationIdProperty } else { [System.Windows.Automation.AutomationElement]::NameProperty }
        $value = if ($id) { $id } else { $name }
        $condition = [System.Windows.Automation.PropertyCondition]::new($property, $value)
        $element = $script:ui.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -eq $element) { throw "Control not found: $value" }
        return $element
    }
    function Click-Ui($element) {
        if ($element.Current.IsOffscreen) {
            $invoke = $null
            if ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invoke)) { $invoke.Invoke(); Start-Sleep -Milliseconds 400; return }
        }
        $rect = $element.Current.BoundingRectangle
        if ($rect.IsEmpty -or $rect.Width -eq 0) { throw 'Control has no visible bounds.' }
        [ReviewWindow]::SetCursorPos([int]($rect.X + $rect.Width / 2), [int]($rect.Y + $rect.Height / 2)) | Out-Null
        [ReviewWindow]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
        [ReviewWindow]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 350
    }
    function Set-Text($element, [string]$text) {
        $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
        Start-Sleep -Milliseconds 350
    }
    function Save-Shot([string]$name) {
        $rect = $script:ui.Current.BoundingRectangle
        $bitmap = [System.Drawing.Bitmap]::new([int]$rect.Width, [int]$rect.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $bitmap.Size)
            $bitmap.Save((Join-Path $OutputDirectory ($name + '.png')))
        }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
    if (!(Test-Path -LiteralPath (Join-Path $dataDirectory 'equora.db'))) { throw 'Isolated database was not created. Run a Debug build only.' }
    $before = (Find-Ui '搜索当前清单').Current.BoundingRectangle.X
    Click-Ui (Find-Ui '展开或收起导航')
    $after = (Find-Ui '搜索当前清单').Current.BoundingRectangle.X
    Start-Sleep -Milliseconds 300
    Save-Shot 'navigation-compact'
    if ($before - $after -lt 100) { throw 'Collapsing navigation did not release content width.' }
    Click-Ui (Find-Ui '展开或收起导航')
    Set-Text (Find-Ui '新任务标题') '界面验收任务'
    Click-Ui (Find-Ui '添加任务')
    Find-Ui '界面验收任务' | Out-Null
    Click-Ui (Find-Ui '完成或重新打开任务')
    Click-Ui (Find-Ui '已完成')
    Find-Ui '界面验收任务' | Out-Null
    Click-Ui (Find-Ui '删除任务')
    Click-Ui (Find-Ui '删除')
    Click-Ui (Find-Ui '回收站')
    Find-Ui '界面验收任务' | Out-Null
    Click-Ui (Find-Ui '恢复')
    Click-Ui (Find-Ui '全部任务')
    Find-Ui '界面验收任务' | Out-Null
    Click-Ui (Find-Ui '界面验收任务')
    Save-Shot 'tasks'
    Set-Text (Find-Ui '新任务标题') '永久删除验收任务'
    Click-Ui (Find-Ui '添加任务')
    Click-Ui (Find-Ui '永久删除验收任务')
    $trashTask = Find-Ui '永久删除验收任务'
    $row = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($trashTask)
    while ($row -and $row.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem) { $row = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($row) }
    $delete = $row.FindFirst([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, '删除任务'))
    Click-Ui $delete
    Click-Ui (Find-Ui '删除')
    Click-Ui (Find-Ui '回收站')
    Click-Ui (Find-Ui '删除任务')
    Click-Ui (Find-Ui '取消')
    Find-Ui '永久删除验收任务' | Out-Null
    Click-Ui (Find-Ui '删除任务')
    Click-Ui (Find-Ui '永久删除')
    $remainingTask = $script:ui.FindFirst([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, '永久删除验收任务'))
    if ($remainingTask) { throw 'Permanently deleted task remains visible.' }
    Click-Ui (Find-Ui '全部任务')
    Click-Ui (Find-Ui '界面验收任务')
    Click-Ui (Find-Ui '设置')
    Set-Text (Find-Ui '' 'AccentHex') '#0078D4'
    Click-Ui (Find-Ui '应用')
    $preferences = Get-Content (Join-Path $dataDirectory 'preferences.json') -Raw | ConvertFrom-Json
    if ($preferences.Accent -ne '#0078D4') { throw 'Accent was not saved.' }
    Save-Shot 'settings'
    Click-Ui (Find-Ui '深色')
    Save-Shot 'settings-dark'
    Click-Ui (Find-Ui '浅色')
    foreach ($page in @(@('首页', 'home'), @('日历', 'calendar'), @('四象限', 'matrix'), @('专注', 'focus'))) {
        Click-Ui (Find-Ui $page[0])
        Save-Shot $page[1]
    }
    Click-Ui (Find-Ui '开始')
    Start-Sleep -Milliseconds 1200
    Click-Ui (Find-Ui '暂停')
    $pausedClock = (Find-Ui '' 'FocusClock').Current.Name
    Start-Sleep -Milliseconds 2200
    if ((Find-Ui '' 'FocusClock').Current.Name -ne $pausedClock) { throw 'Paused clock kept advancing.' }
    Click-Ui (Find-Ui '首页')
    Click-Ui (Find-Ui '专注')
    if ((Find-Ui '' 'FocusClock').Current.Name -ne $pausedClock) { throw 'Paused clock changed after navigating.' }
    Click-Ui (Find-Ui '继续')
    Find-Ui '暂停' | Out-Null
    Click-Ui (Find-Ui '结束')
    Find-Ui '开始' | Out-Null
    $modeChoice = (Find-Ui '模式').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $modeChoice.Expand()
    Start-Sleep -Milliseconds 400
    Save-Shot 'dropdown'
    $modeChoice.Collapse()
    Start-Sleep -Milliseconds 350
    Click-Ui (Find-Ui '应用与网站限制')
    Save-Shot 'restrictions'
    Click-Ui (Find-Ui '读取已安装的应用')
    $readMessage = $null
    for ($readAttempt = 0; $readAttempt -lt 30 -and !$readMessage; $readAttempt++) {
        Start-Sleep -Seconds 1
        $readMessage = $script:ui.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -match '^已读取 [1-9][0-9]* 个应用' }
    }
    if (!$readMessage) { throw 'Installed application discovery did not return applications.' }
    Set-Text (Find-Ui '' 'RuleName') '测试应用限制'
    Set-Text (Find-Ui '' 'RuleTarget') 'equora-review-probe.exe'
    (Find-Ui '' 'FocusOnly').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    (Find-Ui '保存规则').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 500
    $rules = Get-Content (Join-Path $dataDirectory 'restrictions.json') -Raw | ConvertFrom-Json
    if ($rules.rules[0].target -ne 'equora-review-probe.exe') { throw 'Restriction was not persisted.' }
    Click-Ui (Find-Ui '专注')
    Click-Ui (Find-Ui '开始')
    $probeExe = Join-Path $dataDirectory 'equora-review-probe.exe'
    Add-Type -TypeDefinition 'using System; using System.Windows.Forms; public class EquoraReviewProbe { [STAThread] public static void Main() { Application.Run(new Form { Text = "Equora restriction test", Width = 400, Height = 200 }); } }' -ReferencedAssemblies System.Windows.Forms -OutputAssembly $probeExe -OutputType WindowsApplication
    $probe = Start-Process -FilePath $probeExe -WindowStyle Normal -PassThru
    try {
        Start-Sleep -Seconds 1
        $probe.Refresh()
        $probeWindow = $probe.MainWindowHandle
        if ($probeWindow -eq 0) { throw 'The test probe did not create a visible window.' }
        [ReviewWindow]::ShowWindow($probeWindow, 9) | Out-Null
        [ReviewWindow]::SetForegroundWindow($probeWindow) | Out-Null
        Start-Sleep -Seconds 2
        if (![ReviewWindow]::IsIconic($probeWindow)) { throw 'Restricted foreground window was not minimized.' }
        Click-Ui (Find-Ui '暂停')
        [ReviewWindow]::ShowWindow($probeWindow, 9) | Out-Null
        [ReviewWindow]::SetForegroundWindow($probeWindow) | Out-Null
        Start-Sleep -Seconds 2
        if ([ReviewWindow]::IsIconic($probeWindow)) { throw 'Focus-only restriction remained after pausing focus.' }
    }
    finally { $probe.CloseMainWindow() | Out-Null }
    Click-Ui (Find-Ui '应用与网站限制')
    Click-Ui (Find-Ui '暂停所有限制 15 分钟')
    $pauseNotice = Find-Ui '' 'PauseNotice'
    if ($pauseNotice.Current.IsOffscreen) { throw 'Pause countdown is not visible.' }
    Save-Shot 'restrictions-paused'
    Click-Ui (Find-Ui '任务')
    if ((Find-Ui '' 'PauseNotice').Current.IsOffscreen) { throw 'Pause countdown disappeared after navigation.' }
    Click-Ui (Find-Ui '专注')
    [ReviewWindow]::SetForegroundWindow($handle) | Out-Null
    Click-Ui (Find-Ui '结束')
    Click-Ui (Find-Ui '任务')
    foreach ($kind in @('项目', '标签')) {
        Click-Ui (Find-Ui ($kind + ' · 管理'))
        Set-Text (Find-Ui '' 'WorkspaceName') ($kind + '验收')
        Click-Ui (Find-Ui '保存')
        Find-Ui ($kind + '验收') | Out-Null
        Set-Text (Find-Ui '' 'WorkspaceName') ($kind + '已修改')
        Click-Ui (Find-Ui '保存')
        Find-Ui ($kind + '已修改') | Out-Null
        Save-Shot ('manage-' + $(if ($kind -eq '项目') { 'projects' } else { 'tags' }))
        (Find-Ui '确认删除选中分类（任务将保留）').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
        Click-Ui (Find-Ui '删除选中项')
        Find-Ui '分类已删除，任务保留。' | Out-Null
        Click-Ui (Find-Ui '关闭')
    }
    Click-Ui (Find-Ui '日历')
    Click-Ui (Find-Ui '新建时间段')
    Set-Text (Find-Ui '' 'BlockNote') '验收时间段'
    Set-Text (Find-Ui '' 'BlockColor') '#008272'
    Click-Ui (Find-Ui '保存')
    $scroll = (Find-Ui '' 'Scroller').GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    $scroll.SetScrollPercent(-1, 55)
    Start-Sleep -Milliseconds 400
    Save-Shot 'calendar-block'
    function Find-Block([string]$prefix) {
        $found = $script:ui.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name.StartsWith($prefix) } | Select-Object -First 1
        if (!$found) { throw "Calendar block missing: $prefix" }; return $found
    }
    function RightClick-Ui($element) {
        $rect = $element.Current.BoundingRectangle
        [ReviewWindow]::SetCursorPos([int]($rect.X + $rect.Width / 2), [int]($rect.Y + 8)) | Out-Null
        [ReviewWindow]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero)
        [ReviewWindow]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 400
    }
    RightClick-Ui (Find-Block '验收时间段')
    Click-Ui (Find-Ui '编辑时间段')
    Set-Text (Find-Ui '' 'BlockNote') '已编辑时间段'
    Click-Ui (Find-Ui '保存')
    RightClick-Ui (Find-Block '已编辑时间段')
    Click-Ui (Find-Ui '删除时间段')
    Click-Ui (Find-Ui '删除')
    Click-Ui (Find-Ui '设置')
    $closeChoice = (Find-Ui '' 'CloseChoice').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $closeChoice.Expand(); Start-Sleep -Milliseconds 300
    (Find-Ui '隐藏窗口，在托盘继续运行').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $closeChoice.Collapse(); Start-Sleep -Milliseconds 400
    Click-Ui (Find-Ui '专注'); Click-Ui (Find-Ui '开始')
    $app.CloseMainWindow() | Out-Null
    Start-Sleep -Seconds 2
    $app.Refresh()
    if ($app.HasExited -or [ReviewWindow]::IsWindowVisible($handle)) { throw 'Close-to-tray did not hide the window.' }
    $pulse = Get-Content (Join-Path $dataDirectory 'restriction-heartbeat.json') -Raw | ConvertFrom-Json
    if (!$pulse.focusing) { throw 'Focus stopped while in tray.' }
    $priorData = $env:EQUORA_TEST_DATA_DIRECTORY
    $env:EQUORA_TEST_DATA_DIRECTORY = $dataDirectory
    try { $second = Start-Process -FilePath $Exe -ArgumentList '--data-directory', ('"' + [IO.Path]::GetFullPath($dataDirectory) + '"') -WindowStyle Hidden -PassThru }
    finally { $env:EQUORA_TEST_DATA_DIRECTORY = $priorData }
    if (!$second.WaitForExit(10000)) { $second.Kill(); throw 'Second instance did not redirect to the tray instance.' }
    Start-Sleep -Milliseconds 500
    if (![ReviewWindow]::IsWindowVisible($handle)) { throw 'Tray restore failed.' }
    Click-Ui (Find-Ui '结束'); Click-Ui (Find-Ui '设置')
    $closeChoice = (Find-Ui '' 'CloseChoice').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $closeChoice.Expand(); Start-Sleep -Milliseconds 300
    (Find-Ui '退出程序').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $closeChoice.Collapse(); Start-Sleep -Milliseconds 400
    Click-Ui (Find-Ui '任务')
    [ReviewWindow]::MoveWindow($handle, 20, 20, 900, 760, $true) | Out-Null
    Start-Sleep -Milliseconds 500
    $backButton = $script:ui.FindFirst([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, '返回任务列表'))
    if ($backButton -and !$backButton.Current.IsOffscreen) { Click-Ui $backButton }
    Find-Ui '界面验收任务' | Out-Null
    Save-Shot 'tasks-narrow'
    'PASS: navigation, task lifecycle, themes, focus pause, restrictions, discovery, classification CRUD, calendar CRUD, close-to-tray, background focus and single-instance restore.'
    "Screenshots: $OutputDirectory"
}
finally {
    if ($app -and !$app.HasExited) { $app.CloseMainWindow() | Out-Null; if (!$app.WaitForExit(2000)) { $app.Kill() } }
}
