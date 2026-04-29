# ETL-SQL VS Code Extension

Language support for ETL-SQL scripts with lineage tracking, diagnostics, and formatting.

## Installation

### 1. Developer Mode (F5)
If you are developing the extension:
1. Open this folder (`src/etl-sql-vscode`) in VS Code.
2. Run `npm install`.
3. Press **F5** to start a new "Extension Development Host" window.

### 2. Permanent Local Installation (Windows)
To use the extension in all your VS Code windows without needing F5:
1. Open PowerShell as **Administrator**.
2. Run the following command (adjusting paths if necessary):
   ```powershell
   New-Item -ItemType Junction -Path "$HOME\.vscode\extensions\etl-sql-vscode" -Target "C:\Users\chuck\scratch\ETL-SQL\src\etl-sql-vscode"
   ```
3. Restart VS Code.

## Configuration
Go to VS Code Settings (`Ctrl+,`) and search for **ETL-SQL**:
- **Server Path**: Path to `ETL-SQL-LSP.exe` (if not detected in build folder).
- **Executable Path**: Path to `ETL-SQL.exe` (if not detected in build folder).

## Features
- **Format Document** (Alt+Shift+F): Uses the engine's SqlFormatter.
- **Run Script**: Executes the current script via CLI.
- **Lineage Hover**: Shows column lineage graphs on hover.
- **IntelliSense**: Keyword and function completions.
