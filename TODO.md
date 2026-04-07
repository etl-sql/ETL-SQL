
## Syntax Additions and Improvements

## VS CODE Bugs/Improvements

## Doc Review Pending Items

**DR-1. Console Editor (`ui edit`) Command** — *Pending*  
The README describes a full terminal-based editor launched via `dotnet run -- --ui edit MyScript.etlsql`, including shortcuts like `F5`, `Shift+F5`, `Ctrl+I`, `F1`. The language reference has no section for how to launch or use the console editor. Consider adding a "Getting Started / Running Scripts" section to the reference doc.

**DR-2. VS Code Extension** — *Pending*  
The README mentions a dedicated VS Code language server extension. The language reference has no mention of it at all — not even a pointer to where to install it.

**DR-3. Native SQL Pushdown Guide** — *Pending — needs content decision*  
README claims automatic pushdown of joins/filters to source databases. The language reference has no guide explaining when pushdown is triggered, how to force it, or how to prevent it. The `EXECUTE...BEGIN...END` walkthrough touches it informally but there's no clear guide. Add a "Performance / Pushdown" section explaining the rules.
