# Desktop UI refresh verification

The desktop shell uses a compact inline SplitView: expanding navigation reserves
space instead of covering the current page. Page layouts use their actual available
width. Tasks switch between list and detail on compact windows; the calendar keeps
its time grid horizontally scrollable when space is limited.

Icons use Segoe MDL2 Assets. Theme mode, accent color and privacy preferences are
stored in `preferences.json` alongside the database, including unpackaged builds.
The settings page supports preset accents and custom `#RRGGBB` values. High contrast
uses the operating system's colors.

Task creation uses a separate draft field. Complete, delete, restore and undo keep
the current selection and detail state consistent. Loading task details no longer
triggers automatic saves. Deleted task details are read-only. Search is debounced,
and Ctrl+Z invokes the task undo stack.

## Checks

From the repository root:

```powershell
dotnet build desktop/Equora.App/Equora.App.csproj --no-restore -p:Platform=x64 -m:1 -nr:false
dotnet test desktop/Equora.App.Tests/Equora.App.Tests.csproj --no-restore -m:1 -nr:false --filter 'FullyQualifiedName!~StartupRegistrationTests'
powershell -NoProfile -ExecutionPolicy Bypass -File desktop/tools/ui-review.ps1
```

- App build: passed. NuGet vulnerability metadata was unavailable on this machine (NU1900); compilation succeeded.
- Automated tests: 126 passed. The existing startup-registration test is excluded
  because it writes the user's Windows Run registry key and is unrelated to this change.
- GUI check: passed navigation reflow, creation, completion, deletion, trash restore,
  accent persistence and navigation to all pages. Screenshots were inspected for
  task details, compact tasks, settings, light/dark appearance, home, calendar and focus.
- Screenshot output: `artifacts/ui-review/` (not tracked).

The follow-up GUI check also passed frozen pause/resume across navigation, installed
application discovery, rule persistence, actual minimization of a dedicated test
window during focus and release while paused, and project/tag create/rename/delete.
The dropdown screenshot confirms square neutral selection styling. Extension API
tests (2) and the shipped native-host protocol test passed; see
[`usage-restrictions.md`](usage-restrictions.md) for browser installation and scope.

Follow-up behavior and keyboard shortcuts are documented in [`help.md`](help.md).
New tests cover automatic work/rest rounds, pausing rest, distinct focus modes,
calendar notes/color/time updates and preservation of tasks on block deletion,
and data-directory snapshot migration with non-overwrite guards. The existing
Windows startup registry is not changed by the automated checks.

The final isolated GUI run passed calendar create/edit/delete, closing the window
to the tray while retaining active focus, restoring the existing instance by
launching the executable again, and returning close behavior to normal exit.
The compact navigation screenshot was inspected for centered icons. File and folder
pickers are connected but were not automated end-to-end; migration itself is covered
by the database snapshot test. The actual user startup registration and data-directory
locator were not modified during verification.

The GUI check launches its own window and closes it afterwards. Debug builds alone
accept `EQUORA_TEST_DATA_DIRECTORY`; the script sets it for the test process to keep
the user's tasks and preferences untouched. It refuses to edit tasks unless the
isolated database exists. Tests leave their scratch data under the artifact folder
for inspection. The file-picker flows are wired to the desktop window and report
errors; they were not exercised end-to-end in the automated GUI check.
