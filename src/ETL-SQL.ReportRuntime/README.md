# ETL-SQL.ReportRuntime

This source area owns the canonical browser runtime assets shared by ReportPlayer, Report Portal, and the VS Code report preview.

Edit files under `Resources/Shared/`, then run:

```powershell
.\scripts\sync-assets.ps1
.\scripts\sync-assets.ps1 -Check
```

Generated host copies live under the host projects and should not be edited directly.
