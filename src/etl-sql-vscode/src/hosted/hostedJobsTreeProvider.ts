import * as vscode from 'vscode';
import {
    HostedJobItemDto,
    HostedJobRecentRunDto,
    HostedJobRunDetailDto,
    HostedJobRunStatementDto,
    HostedSessionState,
    redactSecrets,
    resolveTrustedPortalUrl
} from './hostedTypes';
import { HostedSessionExpiredError, HostedSessionManager } from './hostedSessionManager';
import { HostedPipelineClient } from './hostedPipelineClient';

export type HostedTreeNode =
    | { kind: 'placeholder'; id: string; label: string; description?: string; commandId?: string }
    | { kind: 'more'; id: string; sessionKey: string; epoch: number; offset: number }
    | { kind: 'job'; id: string; item: HostedJobItemDto; sessionKey: string }
    | { kind: 'run'; id: string; job: HostedJobItemDto; run: HostedJobRecentRunDto; sessionKey: string }
    | { kind: 'statement'; id: string; index: number; statement: HostedJobRunStatementDto }
    | { kind: 'info'; id: string; label: string; description?: string };

export interface HostedJobsTreeOptions {
    sessions: HostedSessionManager;
    client?: HostedPipelineClient;
    runSavedPipeline?: (pipelineId: number, version: number, displayName: string) => Promise<void>;
    openExternal?: (uri: vscode.Uri) => Thenable<boolean> | Promise<boolean>;
    log?: (message: string) => void;
}

/**
 * Read-only VS Code sidebar tree for the connected Portal's authorized jobs, saved pipelines,
 * last run status, next scheduled run, and recent runs with statement logs (ST59).
 *
 * Clears immediately on host, tenant, or account change and discards any late responses from
 * a previous session.
 */
export class HostedJobsTreeProvider implements vscode.TreeDataProvider<HostedTreeNode>, vscode.Disposable {
    private readonly _sessions: HostedSessionManager;
    private readonly _client: HostedPipelineClient;
    private readonly _runSavedPipeline?: HostedJobsTreeOptions['runSavedPipeline'];
    private _openExternal: (uri: vscode.Uri) => Thenable<boolean> | Promise<boolean>;
    private readonly _log: (message: string) => void;

    private readonly _onDidChangeTreeData = new vscode.EventEmitter<HostedTreeNode | undefined | void>();
    public readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    private readonly _disposables: vscode.Disposable[] = [];
    private _sessionEpoch = 0;
    private _lastSessionKey = 'local';
    private _jobs: HostedJobItemDto[] = [];
    private _jobsLoaded = false;
    private _runsByJob = new Map<string, HostedJobRecentRunDto[]>();
    private _detailByRun = new Map<string, HostedJobRunDetailDto>();
    private _nextJobsOffset: number | null = null;
    private _loadingMore = false;

    constructor(options: HostedJobsTreeOptions) {
        this._sessions = options.sessions;
        this._client = options.client ?? new HostedPipelineClient(this._sessions);
        this._runSavedPipeline = options.runSavedPipeline;
        this._openExternal = options.openExternal ?? (uri => vscode.env.openExternal(uri));
        this._log = options.log ?? (() => {});

        this._lastSessionKey = this.computeSessionKey(this._sessions.getSession());
        const sub = this._sessions.onDidChange(() => this.handleSessionChanged());
        this._disposables.push(this._onDidChangeTreeData, { dispose: () => sub.dispose() });
    }

    public dispose(): void {
        for (const d of this._disposables) {
            try {
                d.dispose();
            } catch {
                // ignore
            }
        }
    }

    public getCurrentSessionKey(): string {
        return this.computeSessionKey(this._sessions.getSession());
    }

    public getCachedJobs(): readonly HostedJobItemDto[] {
        return this._jobs;
    }

    public setOpenExternal(opener: (uri: vscode.Uri) => Thenable<boolean> | Promise<boolean>): void {
        this._openExternal = opener;
    }

    public refresh(): void {
        this._sessionEpoch++;
        this._jobs = [];
        this._jobsLoaded = false;
        this._runsByJob.clear();
        this._detailByRun.clear();
        this._nextJobsOffset = null;
        this._loadingMore = false;
        this._onDidChangeTreeData.fire(undefined);
    }

    public getTreeItem(element: HostedTreeNode): vscode.TreeItem {
        if (element.kind === 'more') {
            const item = new vscode.TreeItem('Load more jobs', vscode.TreeItemCollapsibleState.None);
            item.id = element.id;
            item.command = { command: 'etlsql.hostedJobs.loadMore', title: 'Load more jobs', arguments: [element] };
            return item;
        }
        if (element.kind === 'placeholder') {
            const item = new vscode.TreeItem(element.label, vscode.TreeItemCollapsibleState.None);
            item.description = element.description;
            item.contextValue = 'hosted-placeholder';
            item.iconPath = new vscode.ThemeIcon('cloud');
            if (element.commandId) {
                item.command = { command: element.commandId, title: element.label };
            }
            return item;
        }

        if (element.kind === 'info') {
            const item = new vscode.TreeItem(element.label, vscode.TreeItemCollapsibleState.None);
            item.description = element.description;
            item.contextValue = 'hosted-info';
            item.iconPath = new vscode.ThemeIcon('info');
            return item;
        }

        if (element.kind === 'job') {
            const j = element.item;
            const item = new vscode.TreeItem(j.displayName, vscode.TreeItemCollapsibleState.Collapsed);
            item.id = element.id;
            const parts: string[] = [];
            if (j.kind === 'pipeline' && j.pipelineVersion) {
                parts.push(`v${j.pipelineVersion}`);
            }
            parts.push(j.lastStatus ?? 'Never run');
            if (j.nextScheduledRunAt) {
                parts.push(`Next: ${this.formatShortTime(j.nextScheduledRunAt)}`);
            }
            item.description = parts.join(' · ');
            item.tooltip = [
                `${j.kind === 'pipeline' ? 'Saved Pipeline' : 'Scheduled Job'}: ${j.displayName}`,
                `Job: ${j.jobName}`,
                j.folderPath ? `Folder: ${j.folderPath}` : null,
                `Last Status: ${j.lastStatus ?? 'Never run'}`,
                j.nextScheduledRunAt ? `Next Scheduled Run: ${j.nextScheduledRunAt}` : null
            ].filter(Boolean).join('\n');
            item.contextValue = this.jobContextValue(j);
            item.iconPath = new vscode.ThemeIcon(this.statusIconId(j.lastStatus, j.kind === 'pipeline' ? 'file-code' : 'clock'));
            return item;
        }

        if (element.kind === 'run') {
            const r = element.run;
            const item = new vscode.TreeItem(`Run #${r.runId} · ${r.status}`, vscode.TreeItemCollapsibleState.Collapsed);
            item.id = element.id;
            const descParts = [`${r.rowsProcessed} rows`];
            if (typeof r.durationMs === 'number') {
                descParts.push(`${r.durationMs} ms`);
            }
            descParts.push(this.formatShortTime(r.startTime));
            item.description = descParts.join(' · ');
            item.tooltip = r.errorMessage
                ? `Run #${r.runId} (${r.status}): ${r.errorMessage}`
                : `Run #${r.runId} (${r.status}) — ${r.rowsProcessed} rows`;
            item.contextValue = r.canStop ? 'hosted-run-stoppable' : 'hosted-run-readonly';
            item.iconPath = new vscode.ThemeIcon(this.statusIconId(r.status, 'history'));
            return item;
        }

        const stmt = element.statement;
        const preview = stmt.statement.length > 80 ? `${stmt.statement.slice(0, 80)}…` : stmt.statement;
        const item = new vscode.TreeItem(
            `[${element.index}] ${stmt.failed ? 'Failed' : 'Ran'}: ${preview}`,
            vscode.TreeItemCollapsibleState.None
        );
        item.description = `${stmt.rowsProcessed} rows · ${stmt.durationMs} ms`;
        item.contextValue = 'hosted-statement';
        item.iconPath = new vscode.ThemeIcon(stmt.failed ? 'error' : 'pass');
        return item;
    }

    public async getChildren(element?: HostedTreeNode): Promise<HostedTreeNode[]> {
        const session = this._sessions.getSession();
        if (!session) {
            return [{
                kind: 'placeholder',
                id: 'hosted-connect-placeholder',
                label: 'Connect to a Hosted Portal…',
                description: 'Local (Offline)',
                commandId: 'etlsql.hosted.selectTarget'
            }];
        }

        const epoch = this._sessionEpoch;
        const sessionKey = this.computeSessionKey(session);

        if (!element) {
            if (!this._jobsLoaded) {
                try {
                    const page = await this._client.listHostedJobs(50, 0);
                    if (this.isStale(epoch, sessionKey)) {
                        return [];
                    }
                    if (!this._jobsLoaded) {
                        this._jobs = page.items;
                        this._jobsLoaded = true;
                        this._nextJobsOffset = page.hasMore && page.items.length > 0 ? page.items.length : null;
                    }
                } catch (err) {
                    if (this.isStale(epoch, sessionKey)) {
                        return [];
                    }
                    const msg = this.sanitizeError(err);
                    this._log(`[HostedJobs] Failed to load jobs: ${msg}`);
                    return [{
                        kind: 'info',
                        id: 'hosted-jobs-error',
                        label: err instanceof HostedSessionExpiredError ? 'Hosted session expired — sign in again' : 'Could not load hosted jobs',
                        description: msg
                    }];
                }
            }

            if (this._jobs.length === 0) {
                return [{
                    kind: 'info',
                    id: 'hosted-jobs-empty',
                    label: 'No authorized jobs or pipelines on this host',
                    description: `${session.server.label} (${session.tenantId})`
                }];
            }

            const jobs: HostedTreeNode[] = this._jobs.map(item => ({
                kind: 'job',
                id: `job:${sessionKey}:${item.jobName}`,
                item,
                sessionKey
            }));
            if (this._nextJobsOffset !== null) {
                jobs.push({
                    kind: 'more',
                    id: `more:${sessionKey}:${this._nextJobsOffset}`,
                    sessionKey,
                    epoch,
                    offset: this._nextJobsOffset
                });
            }
            return jobs;
        }

        if (element.kind === 'job') {
            if (element.sessionKey !== sessionKey) {
                return [];
            }
            const cacheKey = `${sessionKey}:${element.item.jobName}`;
            let runs = this._runsByJob.get(cacheKey);
            if (!runs) {
                try {
                    runs = await this._client.listHostedJobRuns(element.item.jobName, 10);
                    if (this.isStale(epoch, sessionKey)) {
                        return [];
                    }
                    this._runsByJob.set(cacheKey, runs);
                } catch (err) {
                    if (this.isStale(epoch, sessionKey)) {
                        return [];
                    }
                    const msg = this.sanitizeError(err);
                    return [{
                        kind: 'info',
                        id: `runs-err:${cacheKey}`,
                        label: 'Could not load recent runs',
                        description: msg
                    }];
                }
            }

            if (runs.length === 0) {
                return [{
                    kind: 'info',
                    id: `runs-empty:${cacheKey}`,
                    label: 'No recent runs recorded'
                }];
            }

            return runs.map(run => ({
                kind: 'run',
                id: `run:${sessionKey}:${element.item.jobName}:${run.runId}`,
                job: element.item,
                run,
                sessionKey
            }));
        }

        if (element.kind === 'run') {
            if (element.sessionKey !== sessionKey) {
                return [];
            }
            const cacheKey = `${sessionKey}:${element.job.jobName}:${element.run.runId}`;
            let detail = this._detailByRun.get(cacheKey);
            if (!detail) {
                try {
                    detail = await this._client.getHostedJobRunDetail(element.job.jobName, element.run.runId);
                    if (this.isStale(epoch, sessionKey)) {
                        return [];
                    }
                    this._detailByRun.set(cacheKey, detail);
                } catch (err) {
                    if (this.isStale(epoch, sessionKey)) {
                        return [];
                    }
                    const msg = this.sanitizeError(err);
                    return [{
                        kind: 'info',
                        id: `detail-err:${cacheKey}`,
                        label: 'Could not load statement logs',
                        description: msg
                    }];
                }
            }

            const nodes: HostedTreeNode[] = detail.statements.map((stmt, idx) => ({
                kind: 'statement',
                id: `stmt:${cacheKey}:${idx}`,
                index: idx + 1,
                statement: stmt
            }));

            if (detail.logTruncated) {
                nodes.push({
                    kind: 'info',
                    id: `trunc:${cacheKey}`,
                    label: 'Statement log truncated (open in Portal for full history)'
                });
            }
            if (detail.errorMessage) {
                nodes.push({
                    kind: 'info',
                    id: `err:${cacheKey}`,
                    label: `Error: ${detail.errorMessage}`
                });
            }
            if (nodes.length === 0) {
                nodes.push({
                    kind: 'info',
                    id: `nostmts:${cacheKey}`,
                    label: `No statement logs recorded (${detail.status})`
                });
            }
            return nodes;
        }

        return [];
    }

    public async loadMore(node?: HostedTreeNode): Promise<void> {
        if (node?.kind !== 'more' || this._loadingMore || this._nextJobsOffset !== node.offset
            || this.isStale(node.epoch, node.sessionKey)) {
            return;
        }

        this._loadingMore = true;
        try {
            const page = await this._client.listHostedJobs(50, node.offset);
            if (this.isStale(node.epoch, node.sessionKey)) {
                return;
            }

            const jobsByName = new Map(this._jobs.map(item => [item.jobName, item]));
            for (const item of page.items) {
                jobsByName.set(item.jobName, item);
            }
            this._jobs = [...jobsByName.values()];
            this._nextJobsOffset = page.hasMore && page.items.length > 0 ? node.offset + page.items.length : null;
            this._onDidChangeTreeData.fire(undefined);
        } catch (err) {
            if (!this.isStale(node.epoch, node.sessionKey)) {
                void vscode.window.showErrorMessage(`ETL-SQL: Could not load more jobs. ${this.sanitizeError(err)}`);
            }
        } finally {
            if (!this.isStale(node.epoch, node.sessionKey)) {
                this._loadingMore = false;
            }
        }
    }

    public async openInPortal(node?: HostedTreeNode): Promise<string | null> {
        const session = this._sessions.getSession();
        if (!session) {
            vscode.window.showWarningMessage('ETL-SQL: Connect to a hosted Portal before opening a link.');
            return null;
        }

        let relativePath: string;
        if (node?.kind === 'job') {
            if (node.sessionKey !== this.computeSessionKey(session)) {
                return null;
            }
            relativePath = node.item.portalPath;
        } else if (node?.kind === 'run') {
            if (node.sessionKey !== this.computeSessionKey(session)) {
                return null;
            }
            relativePath = node.run.portalPath;
        } else {
            relativePath = '/orchestrator.html';
        }

        const trustedUrl = resolveTrustedPortalUrl(session.server.origin, relativePath);
        if (!trustedUrl) {
            vscode.window.showErrorMessage('ETL-SQL: Refused to open a link outside the connected trusted Portal origin.');
            return null;
        }

        await this._openExternal(vscode.Uri.parse(trustedUrl));
        return trustedUrl;
    }

    public async runNode(node?: HostedTreeNode): Promise<boolean> {
        const session = this._sessions.getSession();
        if (!session || node?.kind !== 'job' || !node.item.canRun) {
            return false;
        }
        const sessionKey = this.computeSessionKey(session);
        if (node.sessionKey !== sessionKey) {
            vscode.window.showWarningMessage('ETL-SQL: Hosted target or account changed. Refresh and try again.');
            return false;
        }

        const item = node.item;
        try {
            if (item.kind === 'pipeline') {
                if (!item.pipelineId || !item.pipelineVersion || !this._runSavedPipeline) {
                    return false;
                }
                await this._runSavedPipeline(item.pipelineId, item.pipelineVersion, item.displayName);
            } else {
                const triggered = await this._client.triggerHostedJob(item.jobName);
                if (this.getCurrentSessionKey() !== sessionKey) {
                    return false;
                }
                void vscode.window.showInformationMessage(`ETL-SQL: ${triggered.message}`);
            }

            if (this.computeSessionKey(this._sessions.getSession()) === sessionKey) {
                this.refresh();
            }
            return true;
        } catch (err) {
            if (this.getCurrentSessionKey() === sessionKey) {
                void vscode.window.showErrorMessage(`ETL-SQL: ${this.sanitizeError(err)}`);
            }
            return false;
        }
    }

    public async stopNode(node?: HostedTreeNode): Promise<boolean> {
        const session = this._sessions.getSession();
        if (!session || !node) {
            return false;
        }
        const sessionKey = this.computeSessionKey(session);
        let jobName: string | null = null;
        let runId: number | null = null;

        if (node.kind === 'run' && node.sessionKey === sessionKey && node.run.canStop) {
            jobName = node.job.jobName;
            runId = node.run.runId;
        } else if (node.kind === 'job' && node.sessionKey === sessionKey && node.item.canStop && node.item.activeRunId) {
            jobName = node.item.jobName;
            runId = node.item.activeRunId;
        }

        if (!jobName || !runId) {
            return false;
        }

        try {
            const cancelled = await this._client.cancelHostedJobRun(jobName, runId);
            if (this.getCurrentSessionKey() !== sessionKey) {
                return false;
            }
            void vscode.window.showInformationMessage(`ETL-SQL: ${cancelled.message}`);
            if (this.computeSessionKey(this._sessions.getSession()) === sessionKey) {
                this.refresh();
            }
            return true;
        } catch (err) {
            if (this.getCurrentSessionKey() === sessionKey) {
                void vscode.window.showErrorMessage(`ETL-SQL: ${this.sanitizeError(err)}`);
            }
            return false;
        }
    }

    private handleSessionChanged(): void {
        const nextKey = this.computeSessionKey(this._sessions.getSession());
        if (nextKey !== this._lastSessionKey) {
            this._lastSessionKey = nextKey;
            this.refresh();
        }
    }

    private computeSessionKey(session: HostedSessionState | null): string {
        if (!session) {
            return 'local';
        }
        return `${session.server.origin}|${session.tenantId}|${session.user.id}|${session.sessionId}`;
    }

    private isStale(epoch: number, sessionKey: string): boolean {
        return epoch !== this._sessionEpoch || this.computeSessionKey(this._sessions.getSession()) !== sessionKey;
    }

    private jobContextValue(job: HostedJobItemDto): string {
        if (job.canRun && job.canStop) {
            return 'hosted-job-run-stop';
        }
        if (job.canRun) {
            return 'hosted-job-run';
        }
        if (job.canStop) {
            return 'hosted-job-stop';
        }
        return 'hosted-job-readonly';
    }

    private statusIconId(status: string | null | undefined, fallback: string): string {
        const upper = (status || '').toUpperCase();
        if (upper === 'SUCCESS') {
            return 'pass';
        }
        if (upper === 'FAILED' || upper === 'FAILURE') {
            return 'error';
        }
        if (upper === 'CANCELLED') {
            return 'circle-slash';
        }
        if (upper === 'RUNNING' || upper === 'QUEUED' || upper === 'PENDING') {
            return 'sync~spin';
        }
        return fallback;
    }

    private formatShortTime(iso: string): string {
        const d = new Date(iso);
        if (Number.isNaN(d.getTime())) {
            return iso;
        }
        return d.toISOString().replace('T', ' ').slice(0, 16) + ' UTC';
    }

    private sanitizeError(err: unknown): string {
        const raw = err instanceof Error ? err.message : String(err);
        return redactSecrets(raw, this._sessions.getActiveSecrets());
    }
}
