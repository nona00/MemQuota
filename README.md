# MemQuota

MemQuota is a Windows tray application for .NET Framework 4.8. Its icon shows physical memory usage as a slowly animated blue bar, which turns red at the warning threshold. Values are refreshed every two seconds.

Hover over the icon to see the five largest process groups. Left-click the icon to keep the list open; click it again to close the list. The list stays above ordinary windows without taking keyboard focus, including when you open Snipping Tool. Use **Exit** in the icon's context menu to close the application. If Windows hides the icon, it may need to be enabled in the notification area.

## Warning threshold

Set `WarningThresholdPercent` in `MemQuota.exe.config` next to the executable to an integer from 1 to 100. The default is 85. Changes take effect on the next launch; an invalid value is reported at startup.

## Understanding the values

Total usage is the physical memory in use as reported by Windows. The list sums the current physical working sets of readable processes with the same process name; the number in parentheses is the process count. Percentages are relative to total physical memory. One MB is calculated as 1,048,576 bytes.

Unlike the private working set shown in Windows Task Manager, the working set also includes shared memory pages. These can be counted more than once across processes, so process totals and overall usage are not directly comparable. Different programs with the same process name are grouped together. Processes that have exited or cannot be read may be missing from a snapshot and are reported accordingly. A standard icon is used if an application icon is inaccessible.

**Est. freed on exit** is the sum of the current private physical working sets of the processes in a group. It is an estimate of memory potentially freed by exiting all of them, not memory that MemQuota can free or a guaranteed change in total usage. Shared pages, caches, changing workloads and processes that restart can make the actual change different. The estimate is hidden if any process in the group cannot be queried or if Windows does not support the required memory counters. These counters require Windows 10 22H2 or Windows 11 22H2 with the September 2023 cumulative update or later.

**Free Mem** asks Windows to trim the working sets of the currently listed processes in that group; it does not close the application or request that it release its allocations. Windows may page memory out or make it available for reuse, and the application can load it again. The actual savings may be temporary or zero, and performance may suffer. Some processes may deny access or have exited; the result reports successful requests and failures. The estimate for exiting the application is not the expected amount freed by this action.

## Build

Open `MemQuota.slnx` in Visual Studio and build the project. The .NET Framework 4.8 Developer Pack and Windows desktop build tools are required. The executable and its configuration are placed in `bin\Debug` or `bin\Release`.
