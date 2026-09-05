/**
 * Globals the browser sources genuinely have, and that no `import` introduces.
 *
 * Everything here is a real global some file actually assigns — `js/feedback.js` and
 * `js/native-charts.js` are loaded as classic scripts before the page's module, and
 * `acquireVsCodeApi` is injected by the VS Code webview host. Declaring them is what makes
 * "cannot find name" mean *this name is not defined anywhere*, which is the finding worth having.
 * Nothing speculative belongs in this file: an entry added to quiet an error is an entry that
 * stops the gate reporting the next real one.
 */

/** One toast. Returns a function that dismisses it early. See src/ETL-SQL.Portal/wwwroot/js/feedback.js. */
interface EtlSqlFeedbackNotifyOptions {
    tone?: 'info' | 'success' | 'warning' | 'error';
    title?: string;
    /** Milliseconds. 0 or below keeps the toast until it is dismissed. */
    duration?: number;
    action?: { label?: string; onSelect: () => void };
    auditAction?: string | null;
}

interface EtlSqlFeedbackDialogOptions {
    title?: string;
    /** The consequence of going ahead, shown under the message. */
    impact?: string;
    confirmLabel?: string;
    cancelLabel?: string;
    danger?: boolean;
    auditAction?: string;
}

/** `prompt` reads the same object twice — once as the dialog, once as the input's own settings. */
interface EtlSqlFeedbackPromptOptions extends EtlSqlFeedbackDialogOptions {
    /** Label on the input. */
    label?: string;
    /** Initial value. */
    value?: string;
    multiline?: boolean;
    /** Renders as a password field. */
    secret?: boolean;
    autocomplete?: string;
    required?: boolean;
    requiredMessage?: string;
    minLength?: number;
    /** A RegExp, not a string: `openDialog` calls `.test(value)` on it directly. */
    pattern?: RegExp;
    patternMessage?: string;
}

interface EtlSqlFeedback {
    notify(message: string, options?: EtlSqlFeedbackNotifyOptions): () => void;
    confirm(message: string, options?: EtlSqlFeedbackDialogOptions): Promise<boolean>;
    prompt(message: string, options?: EtlSqlFeedbackPromptOptions): Promise<string | null>;
}

/** Apache Arrow, as the vendored `arrow.min.js` bundle exposes it. */
interface EtlSqlArrow {
    tableFromIPC(bytes: Uint8Array | ArrayBuffer): {
        schema: { fields: Array<{ name: string }> };
        numRows: number;
        numCols: number;
        getChildAt(index: number): { get(row: number): unknown } | null;
        [key: string]: unknown;
    };
    [key: string]: unknown;
}

/** ECharts host shim. See src/ETL-SQL.Portal/wwwroot/js/native-charts.js. */
interface EtlSqlNativeCharts {
    init(host: HTMLElement, option?: unknown): unknown;
    getInstanceByDom(host: HTMLElement): unknown;
}

/**
 * A visual's host element, with the properties report-runtime.js hangs off it.
 *
 * The runtime keeps a little per-visual state on the DOM node rather than in a side map, so the
 * node is the handle everything else finds it by: a card carries the visual it was rendered from,
 * and a chart wrapper carries the crosshair callbacks its siblings call to stay in sync. Naming
 * the shape is what lets a cast to it stay honest — a bare `HTMLElement` cast would silently
 * accept a misspelt one of these.
 */
interface EtlSqlVisualHost extends HTMLElement {
    /** The manifest visual this element was rendered from. */
    _visualData?: { name?: string;[key: string]: unknown };
    /** Shared-crosshair callbacks, called by sibling charts on the same page. */
    _updateCrosshair?: (point: { x: number; y: number }) => void;
    _hideCrosshair?: () => void;
    /**
     * The open detail popover's handle, or null when none is open.
     *
     * `destroy` is typed as `Function` rather than `() => void` on purpose: `attachDetailSurface`
     * returns it from several branches and one of them hands back a value it only knows to be
     * callable. Narrowing it here would report that branch as an error in the wrong place.
     */
    _detailSurface?: { destroy?: Function } | null;
}

/**
 * The `Error` the Portal's fetch wrappers throw for a non-OK response.
 *
 * They attach the HTTP status and the parsed problem body to the error so a caller can tell a 403
 * from a 500 without re-reading the response. Declared here rather than at each throw site because
 * both the Portal's `api.js` and the designer's own fetch wrapper do it, and every catch reads it.
 */
interface Error {
    /** HTTP status, when this error came from a response. */
    status?: number;
    /** The parsed response body, when there was one. */
    payload?: unknown;
}

interface Document {
    /**
     * Set by `installDialogAccessibility` so a page and a module that both install it cannot fight.
     */
    __dialogA11yInstalled?: boolean;
}

declare var ETLSQLFeedback: EtlSqlFeedback | undefined;
declare var nativeCharts: EtlSqlNativeCharts | undefined;

/** Injected by the VS Code webview host; absent in every other host. */
declare function acquireVsCodeApi(): {
    postMessage(message: unknown): void;
    getState(): unknown;
    setState(state: unknown): void;
};

interface Window {
    ETLSQLFeedback?: EtlSqlFeedback;
    nativeCharts?: EtlSqlNativeCharts;

    // ── Injected by the host, read by report-runtime.js ────────────────────────────────────
    // The report runtime runs in four hosts and asks these which one it is in. Nothing in this
    // repository assigns the first group: they are the host's half of the contract, written into
    // the page by the Portal's report shell, the VS Code preview panel, or the offline snapshot
    // writer. Every read is guarded, which is why they are optional here.
    /** VS Code webview preview: the manifest, inlined, instead of an /api/manifest call. */
    __MANIFEST__?: unknown;
    /** Portal/Player web mode. */
    __IS_WEB__?: boolean;
    /** Designer preview iframe. */
    __IS_PREVIEW__?: boolean;
    /** Offline `.etlsnap` viewer: a single file that carries its own manifest and has no server. */
    __ETLSNAP__?: boolean;
    /** Older spelling of the offline flag; still read, so still honoured. */
    __OFFLINE__?: boolean;
    /** Report id when the manifest does not carry one. */
    __REPORT_ID__?: string | number;
    /** Page to open on load, when the host wants one other than the first. */
    __INITIAL_PAGE__?: string;
    /** Base path for the report API in web mode, e.g. `/api/reports/42`. */
    __API_BASE__?: string;

    // ── Published by the browser sources themselves ────────────────────────────────────────
    /** The manifest currently rendered. Set on every render by report-runtime.js. */
    __CURRENT_MANIFEST__?: unknown;
    /** Detail-popover hooks, for the tests and the host. */
    __ETLSQL_DETAIL__?: unknown;
    /** Export readiness, polled by the headless export path. */
    __etlSqlReportExportReady?: boolean;
    __etlSqlReportExportState?: unknown;
    __etlSqlReportWhenExportReady?: (timeoutMs?: number) => Promise<unknown>;
    /** Test escape hatch: pure functions exposed for automated testing. */
    __reportRuntime__?: Record<string, unknown>;
    /** The Studio instance, for the browser tests. Set by js/pages/studio.js. */
    __STUDIO__?: unknown;
    /** Set by js/control-plane.js. */
    ControlPlaneUI?: unknown;

    // ── Vendored library globals ───────────────────────────────────────────────────────────
    /** Apache Arrow, from js/arrow.min.js. Loaded as a classic script, so it arrives as a global. */
    arrow?: EtlSqlArrow;

    // ── Browser APIs TypeScript's DOM lib does not carry ───────────────────────────────────
    /** File System Access API. Chromium only; every call site is inside a feature check. */
    showDirectoryPicker?: (options?: unknown) => Promise<unknown>;
}
