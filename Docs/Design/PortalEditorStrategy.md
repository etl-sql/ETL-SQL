# Design Strategy: First-Class Web Editing in SaaS & Large-Farm Deployments

As ETL-SQL scales into large enterprise farms (multiple orchestrators/portals) and SaaS/Cloud multi-tenant models, the developer profile shifts. 

While local developers prefer VS Code and the TUI, centralized enterprise users and SaaS tenants often **cannot or do not want to install local desktop tools**. They expect to author, test, schedule, and secure pipelines entirely within the browser.

This document evaluates whether we should upgrade the Portal's script editor to a "first-class" experience, the architectural trade-offs, and how to implement it safely.

---

## 1. The Strategy: Should We Make the Portal Editor "First-Class"?

### The "No" Argument (Keep it Lite)
* **Accidental Complexity**: Building a web-based IDE (with autocomplete, diagnostics, file explorers, and formatting) is a massive engineering effort. 
* **Resource Strain**: Running a Language Server (LSP) instance on the server for every active browser session consumes significant CPU and RAM.
* **The "Local Dev" Preference**: Professional developers hate web editors. They want their local VS Code settings, custom themes, copilot extensions, and local git configurations.

### The "Yes" Argument (SaaS & Governance Reality)
* **Zero-Install Onboarding**: In a SaaS model, forcing a user to download VS Code, configure a local C# Language Server, set up SSH tunnels, and import connection profiles just to write a simple script is a major barrier to entry.
* **Centralized Security (Zero-Trust)**: In a secure corporate environment, developers cannot connect local VS Code instances to remote database servers directly due to strict firewall rules. The web portal acts as a **secure proxy gateway**. Authoring scripts directly inside the portal's security boundary (using connection configurations and secrets stored in the portal vault) is highly secure.

### The Verdict
**Yes, we must elevate the Portal Editor.** We don't need to duplicate VS Code's entire feature set, but a "blind" text box with simple color highlighting (our current CodeMirror 6 setup) is not sufficient for a premium SaaS product. We must offer **intelligent assistance** (linting, schema autocomplete, and cell runs) in the browser.

---

## 2. Three Architectural Models for Web Editing

To provide a first-class web editing experience without rewriting our entire IDE stack, we can choose between three models:

```
Option 1: Monaco Editor + LSP-over-WebSockets (Recommended)
─────────────────────────────────────────────────────────────────────────────
Browser (Monaco) ──▶ WebSocket ──▶ Portal Server ──▶ C# Language Server (LSP)

Option 2: VS Code Web (vscode.dev) Integration
─────────────────────────────────────────────────────────────────────────────
Portal UI ──▶ IFrame Webview ──▶ vscode.dev + ETL-SQL Web Extension

Option 3: WebAssembly (Wasm) Client-Side Parser
─────────────────────────────────────────────────────────────────────────────
Browser (CodeMirror) ──▶ Runs C# Parser compiled to Wasm locally in browser
```

### Option 1: Monaco Editor + LSP-over-WebSockets (The Standard Approach)
Monaco is the editor engine that powers VS Code. It runs natively in the browser.
* **How it works**: We replace CodeMirror with Monaco in the Portal UI. When a user opens a script, the browser establishes a WebSocket connection back to the Portal server. The Portal launches a lightweight C# Language Server (`ETL-SQL.LanguageServer`) and proxies the LSP JSON-RPC messages over the WebSocket.
* **Gains**: The developer gets the **exact same autocompletes, diagnostics, and hover descriptions** in the browser as they do in VS Code, because both editors are talking to the same C# Language Server.
* **Losses**: Server resource usage increases linearly with active editor sessions.

### Option 2: VS Code Web (vscode.dev) Integration (The SaaS Leader Approach)
Microsoft allows running VS Code entirely in the browser (vscode.dev) as a Progressive Web App (PWA).
* **How it works**: We package our VS Code Extension as a **Web Extension** (running in the browser's JS sandbox). We embed a customized vscode.dev instance inside the Report Portal inside an iframe.
* **Gains**: 100% identical experience to desktop VS Code. Zero duplicate code (the same extension works on desktop and web). Zero server-side LSP resource footprint (everything runs client-side in the browser).
* **Losses**: Higher initial setup complexity to package the C# Language Server components to run client-side in WebAssembly.

### Option 3: CodeMirror 6 + Wasm Parser (The Lightweight Approach)
Keep the lightweight CodeMirror editor, but compile the C# parser/linter assembly to WebAssembly.
* **How it works**: The browser runs the Wasm parser locally. As the user types, the Wasm code compiles the AST, runs linter checks, and highlights syntax errors locally.
* **Gains**: Extremely fast (zero network latency for syntax checks), zero server load.
* **Losses**: No database schema autocomplete (the Wasm parser doesn't have access to active database connection schemas without making network calls).

---

## 3. SaaS Constraints: Resource Throttling & Security

If developers can write and run scripts directly in the browser on a shared SaaS farm, we must enforce strict guardrails:

1. **Design-Time Query Throttling**:
   - In-editor "Run Cell" executions must not bog down the portal.
   - We must enforce a strict `TOP 100` limit on all queries executed interactively in the web designer.
   - Interactive queries should run in isolated worker threads with short timeouts (e.g. max 15 seconds).
2. **Strict RLS & Auditing**:
   - Every interactive run must be audited (`AuditLog` event: `AD_HOC_RUN`) and run under the security context of the logged-in portal user (applying `@@CURRENT_USER` and row-level security predicates).
3. **Write-Back to Source Control**:
   - If the portal is configured with a Git backend, saving a script in the web editor should execute a Git commit on behalf of the user, ensuring the "source-controlled report" promise is preserved.

---

### Recommendation: Monaco + LSP-over-WebSockets

For Phase 1 of our SaaS model, **Option 1 (Monaco + LSP-over-WebSockets)** is the most logical path. 
- It allows us to reuse the existing C# Language Server binary compiled for the server without rewriting it in WebAssembly.
- Monaco provides a premium, familiar editor interface that makes the Report Portal feel like a state-of-the-art BI engineering platform.
- We can mitigate server resource load by starting the LSP process lazily on editor open, and killing it after 10 minutes of idle inactivity.

---

### References
- [Language Server Architecture](../Architecture/LanguageServer.md)
- [Portal UI - Lite Editor Strategy](../Architecture/PortalUI.md#5-technology-choices)
- [Zero-Trust Operations & Security](../Standards/Connectors_Standards.md)
