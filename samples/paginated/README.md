# Paginated Audit Report Sample

This sample demonstrates a comprehensive audit log analysis dashboard using **Tier 2/Tier 3** logic for performance and **Report-SQL** for interactivity.

## Features
- **Dynamic Data Generation**: Includes a PowerShell script to generate 6 months of realistic audit data (simulating 8-5 M-F activity profiles).
- **Interactive Parameters**:
    - `RELDATEPICKER`: Allows users to pick relative date ranges (e.g., "Last 3 months") or specific dates.
    - `MULTISELECT`: Event type filtering using the `LIST` type and `IN` operator.
    - `SLICER`: User-level filtering with an "All" option.
- **Visuals**:
    - `LINE`: Activity volume trend.
    - `HEATMAP`: Hourly/Weekly activity profile (shows the 8-5 work week density).
    - `DONUT`: Event type breakdown.
    - `HBAR`: Most frequently accessed resources.
    - `TABLE`: Paginated log detail.
- **Layout**:
    - **Collapsible Drawer**: The filters are tucked into a `COLLAPSIBLE` container at the top of the page.
    - **Tabbed Navigation**: Separate "Summary" and "Details" pages.

## Usage
1. Generate the data (if not already present):
   ```powershell
   # Data is pre-generated in this sample, but you can re-run:
   powershell -File gen_audit_data.ps1
   ```
2. Build and serve the report:
   ```sh
   ETL-SQL-Report serve samples/paginated/audit_report.rptsql
   ```
