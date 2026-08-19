# UI sandbox (dev-only)

A no-Docker, no-build way to develop and visually check the portal/VS-Code
browser-side components in isolation. It imports the **canonical** source
directly, so editing it + hitting **↻ Reload** shows changes with no
`sync-assets.ps1`, no portal build, and no catalog DB.

Storybook-style: a categorized, searchable sidebar lists **stories** (components) and a fixture picker
drives each with sample data.

## Run

```powershell
pwsh -File tools\ui-sandbox\serve.ps1
```

Serves the repo root over loopback and opens
`http://localhost:8099/tools/ui-sandbox/index.html`. Ctrl+C to stop.
Port in use? `-Port 8100`. Don't auto-open? `-NoOpen`.

## Explorer & Navigation Features

- **Live Search & Filter**: Instant filtering by keyword across title, subtitle, route, and category with search highlighting.
- **Collapsible Category Accordions**: Stories organized into logical functional groups (`Admin & Fleet`, `Control Plane & SaaS`, `Orchestrator & Jobs`, `Governance & Security`, `Lineage & Graphs`, `Designers & Visuals`, `Script Editors & IDE`, `Portal Shell & Views`).
- **Category Filter Chips**: Fast one-click category filtering (`Admin`, `Control Plane`, `Orchestrator`, etc.).
- **Keyboard Navigation**: Press `/` or `Ctrl+K` to search; `↑` / `↓` arrow keys to cycle through stories; `Esc` to clear search.
- **Deep Linking**: URL hash synchronization (`#story=gateways-admin&fixture=healthy-fleet`) allows shareable URLs that restore the exact story and fixture state on reload.
- **Theme Testing**: Toggle between `☀️ Light` and `🌙 Dark` stage themes to inspect components across themes.

## Stories

The sandbox currently hosts 28 stories categorized by domain:

### Admin & Fleet Management
| Story | ID | Component / UI Surface |
|---|---|---|
| **Data Gateways Admin** | `gateways-admin` | On-premises active-active gateway clusters, live node inspection, and token setup |
| **Connections Admin** | `connections-admin` | Shared connection catalog administration |
| **Secrets Admin** | `secrets-admin` | Secret inventory and administration workflows |
| **Datasets Admin** | `datasets-admin` | Staged dataset status and storage manager |
| **Policy Authority Admin** | `policy-authority-admin` | Organization-policy authority and enrollment controls |
| **Portal Operations** | `portal-operations` | System maintenance, node health, and telemetry |
| **Admin Catalog** | `admin-catalog` | Administration view catalog panels |
| **Subscription History** | `subscription-history` | Email/Slack report subscription logs and audits |

### Control Plane & SaaS
| Story | ID | Component / UI Surface |
|---|---|---|
| **Control Plane Dashboard** | `control-plane-dashboard` | Multi-tenant SaaS platform administration and fleet health |
| **Triage Board** | `triage-board` | Fleet operational triage and incident management |

### Orchestrator & Jobs
| Story | ID | Component / UI Surface |
|---|---|---|
| **Orchestrator Admin** | `orchestrator-admin` | Job execution monitoring and lease management |
| **Run Overrides** | `orchestrator-run-overrides` | Dynamic job parameters and execution overrides |
| **Checkpoint & Resume** | `orchestrator-checkpoint-resume` | Step checkpointing and job resumption UI |
| **Orchestrator ACL** | `orchestrator-acl` | Access control lists for jobs and pipelines |

### Governance & Security
| Story | ID | Component / UI Surface |
|---|---|---|
| **Portal Governance Module** | `portal-governance` | Governance overview, work queue, exceptions, badges, glossary, and settings |
| **Data Quality Queue** | `data-quality-queue` | Quarantine queue, row editor, and quality trend panel |

### Lineage & Graphs
| Story | ID | Component / UI Surface |
|---|---|---|
| **Lineage DAG** | `lineage-dag` | Dependency graphs using Apache ECharts (`renderDag()`) |
| **Lineage Catalog** | `lineage-catalog` | Asset inventory and search view with lineage chips |
| **Lineage UI** | `lineage-ui` | Interactive visual pipelines and flow models |

### Designers & Visuals
| Story | ID | Component / UI Surface |
|---|---|---|
| **Report Designer** | `designer` | Portal-based layout designer canvas (`createDesigner()`) |
| **Snapshot Designer** | `snapshot-designer` | Snapshot-backed layout designing using `.etlsnap` package data |

### Script Editors & IDE
| Story | ID | Component / UI Surface |
|---|---|---|
| **Script Editor** | `script-editor` | Monaco/CodeMirror based query editor |
| **Unified Script Editor** | `script-editor-unified` | Stateful editor host with ETL and Report-SQL fixtures |
| **VS Code Webviews** | `vscode-webviews` | VS Code extension frames (Results panel, Report preview, Report designer) |

### Portal Shell & Views
| Story | ID | Component / UI Surface |
|---|---|---|
| **Portal Responsive Shell** | `portal-responsive-shell` | 390px Reports/Admin global drawer, overlay, and focus containment |
| **Portal Studio** | `portal-studio` | Report and pipeline authoring studio workspace |
| **Feedback** | `feedback` | Interactive user feedback and bug reporting widget |

## Portal parity — what's faithful vs mocked

The sandbox imports the **same canonical `designer.js`** the portal ships, so the UI and all
its client logic are identical. What differs is the backend: there is no engine or catalog DB,
so `mockApi.js` answers the `/api/designer/*` endpoints (`parse`, `generate`, `analyze`,
`complete`, `run`, `schema`, `save`, `script-source/commit`, `preview`) with canned responses
that mirror `DesignerController`'s shapes. Treat run/preview **data** as fixtures, not real
execution — everything else (layout, completion, save/commit flow, preview rendering) behaves
exactly as in the portal.
