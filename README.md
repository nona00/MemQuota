# MemQuota

MemQuota is a Windows tray application for .NET Framework 4.8. Its icon shows physical memory usage as a slowly animated blue bar, which turns red at the warning threshold. Values are refreshed every five seconds.

Hover over the icon to see the five largest process groups. The tray tooltip also shows the largest process group other than Windows Memory Compression and its working set, followed by used and available physical memory. Memory Compression remains included in the popup list and system totals. Left-click the icon to keep the list open; click it again to close the list. Click an application's icon to switch to a visible window from that process group; minimized windows are restored. If no suitable window exists or Windows prevents activation, a message is shown. Use the double arrow in the header to expand to the available screen height and scroll through all groups, or collapse back to the top five. The adjacent minimize button hides the popup without exiting the application. The list stays above ordinary windows without taking keyboard focus, including when you open Snipping Tool. Use **Exit** in the icon's context menu to close the application. If Windows hides the icon, it may need to be enabled in the notification area.

## Warning threshold

Set `WarningThresholdPercent` in `MemQuota.exe.config` next to the executable to an integer from 1 to 100. The default is 85. Changes take effect on the next launch; an invalid value is reported at startup.

## Understanding the values

The footer shows physical memory in use (total minus available), total usable physical memory, and available physical memory from Windows `GlobalMemoryStatusEx`. Its percentage is the approximate memory load reported by Windows. GB and MB use binary units (1,073,741,824 and 1,048,576 bytes respectively), matching the unit convention used by Task Manager. Its figures are rounded and both tools sample at different times, so their readings can differ. The list sums the current physical working sets of readable processes with the same process name; the number in parentheses is the process count. Process percentages are relative to total usable physical memory.

Unlike the private working set shown in Windows Task Manager, the working set also includes shared memory pages. These can be counted more than once across processes, so process totals and overall usage are not directly comparable. Different programs with the same process name are grouped together. Processes that have exited or cannot be read may be missing from a snapshot and are reported accordingly. A standard icon is used if an application icon is inaccessible.

The blue bar shows each group's working set as a percentage of total usable physical memory. The amount in parentheses on **Free Mem** is the sum of the current private physical working sets of the processes in the group: an estimate of memory potentially freed by exiting all of them, not the amount expected from pressing the button or a guaranteed change in total usage. Shared pages, caches, changing workloads and processes that restart can make the actual change different. The estimate is hidden if any process in the group cannot be queried or if Windows does not support the required memory counters. These counters require Windows 10 22H2 or Windows 11 22H2 with the September 2023 cumulative update or later.

**Free Mem** asks Windows to trim the working sets of the currently listed processes in that group; it does not close the application or request that it release its allocations. Windows may page memory out or make it available for reuse, and the application can load it again. The actual savings may be temporary or zero, and performance may suffer. Some processes may deny access or have exited; the result reports successful requests and failures. The estimate for exiting the application is not the expected amount freed by this action.

## Build

Open `MemQuota.slnx` in Visual Studio and build the project. The .NET Framework 4.8 Developer Pack and Windows desktop build tools are required. The executable and its configuration are placed in `bin\Debug` or `bin\Release`.
