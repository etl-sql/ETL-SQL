import * as crypto from 'crypto';
import * as path from 'path';
import * as vscode from 'vscode';
import {
    ActiveHostedRun,
    HostedServerEntry,
    PendingSubmissionRecord,
    PipelineRunDto,
    PipelineRunSubmittedDto,
    StudioRunnablePipelineDto,
    normalizeTrustedServerOrigin,
    parseSavedServers,
    redactSecrets
} from './hostedTypes';
import {
    HostedNetworkError,
    HostedSessionExpiredError,
    HostedSessionManager
} from './hostedSessionManager';
import { HostedPipelineClient } from './hostedPipelineClient';
import { ResultsPanel } from '../resultsPanel';

export type HostedSubmissionOutcome =
    | 'accepted'
    | 'recovered'
    | 'stale_revision'
    | 'refused'
    | 'unconfirmed'
    | 'cancelled'
    | 'skipped';

export interface HostedExecutionControllerOptions {
    sessions: HostedSessionManager;
    client?: HostedPipelineClient;
    postResults?: (message: unknown) => void;
    log?: (message: string) => void;
    sleep?: (ms: number) => Promise<void>;
    initialPollDelayMs?: number;
    maxPollDelayMs?: number;
    randomUuid?: () => string;
}

export class HostedExecutionController implements vscode.Disposable {
    public readonly sessions: HostedSessionManager;
    public readonly client: HostedPipelineClient;

    private readonly _postResults: (message: unknown) => void;
    private readonly _log: (message: string) => void;
    private readonly _sleep: (ms: number) => Promise<void>;
    private readonly _initialPollDelayMs: number;
    private readonly _maxPollDelayMs: number;
    private readonly _randomUuid: () => string;

    private readonly _statusBarItem: vscode.StatusBarItem;
    private readonly _disposables: vscode.Disposable[] = [];
    private _activeRun: ActiveHostedRun | null = null;
    private readonly _pendingSubmissions = new Map<string, PendingSubmissionRecord>();
    private _runningCommand = false;

    constructor(options: HostedExecutionControllerOptions) {
        this.sessions = options.sessions;
        this.client = options.client ?? new HostedPipelineClient(this.sessions);
        this._postResults = options.postResults ?? (msg => ResultsPanel.postMessage(msg));
        this._log = options.log ?? (() => {});
        this._sleep = options.sleep ?? (ms => new Promise(resolve => setTimeout(resolve, ms)));
        this._initialPollDelayMs = options.initialPollDelayMs ?? 500;
        this._maxPollDelayMs = options.maxPollDelayMs ?? 2_000;
        this._randomUuid = options.randomUuid ?? (() => crypto.randomUUID());

        this._statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
        this._statusBarItem.command = 'etlsql.hosted.selectTarget';
        this.refreshStatusBar();
        this._statusBarItem.show();

        const sub = this.sessions.onDidChange(() => this.refreshStatusBar());
        this._disposables.push(this._statusBarItem, { dispose: () => sub.dispose() });
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

    public isHostedTarget(): boolean {
        return this.sessions.isHosted();
    }

    public isRunning(): boolean {
        return this._activeRun !== null;
    }

    public getActiveRun(): ActiveHostedRun | null {
        return this._activeRun;
    }

    public getPendingSubmission(): PendingSubmissionRecord | null {
        const session = this.sessions.getSession();
        return session ? this._pendingSubmissions.get(this.submissionScope(session.server.origin, session.tenantId, session.user.id)) ?? null : null;
    }

    private submissionScope(origin: string, tenantId: string, userId: number): string {
        return JSON.stringify([origin, tenantId, userId]);
    }

    private matchesCaller(record: Pick<PendingSubmissionRecord, 'origin' | 'tenantId' | 'userId'>): boolean {
        const current = this.sessions.getSession();
        return current !== null && current.server.origin === record.origin
            && current.tenantId === record.tenantId && current.user.id === record.userId;
    }

    private forgetSubmission(pending: PendingSubmissionRecord): void {
        const scope = this.submissionScope(pending.origin, pending.tenantId, pending.userId);
        if (this._pendingSubmissions.get(scope)?.idempotencyKey === pending.idempotencyKey) {
            this._pendingSubmissions.delete(scope);
        }
    }

    public refreshStatusBar(): void {
        this._statusBarItem.text = this.sessions.formatStatusBarLabel();
        this._statusBarItem.tooltip = this.sessions.isHosted()
            ? 'Active ETL-SQL execution target (Hosted). Click to switch target or disconnect.'
            : 'Active ETL-SQL execution target: Local (Offline). Click to connect to a hosted Portal.';
    }

    public getSavedServers(): HostedServerEntry[] {
        const config = vscode.workspace.getConfiguration('etlsql');
        const inspected = config.inspect<unknown>('hosted.servers');
        // Saved servers live in user-level (Global) settings only — workspace settings may never register URLs.
        const raw = inspected?.globalValue ?? inspected?.defaultValue ?? [];
        return parseSavedServers(raw);
    }

    public async addHostedServerInteractive(): Promise<HostedServerEntry | undefined> {
        const rawOrigin = await vscode.window.showInputBox({
            prompt: 'Enter the hosted Portal origin (HTTPS required; HTTP allowed only for localhost / 127.0.0.1)',
            placeHolder: 'https://portal.corp.example',
            validateInput: value => {
                const check = normalizeTrustedServerOrigin(value);
                return check.ok ? undefined : check.error;
            }
        });
        if (!rawOrigin) {
            return undefined;
        }
        const validated = normalizeTrustedServerOrigin(rawOrigin);
        if (!validated.ok) {
            vscode.window.showErrorMessage(`ETL-SQL: ${validated.error}`);
            return undefined;
        }

        const label = (await vscode.window.showInputBox({
            prompt: 'Friendly label for this hosted environment',
            placeHolder: 'Dev / Test / Prod',
            value: validated.host
        }))?.trim();
        if (!label) {
            return undefined;
        }

        const alias = label.toLowerCase().replace(/[^a-z0-9._-]+/g, '-');
        const entry: HostedServerEntry = { alias, label, origin: validated.origin };
        const existing = this.getSavedServers().filter(s => s.alias.toLowerCase() !== alias.toLowerCase());
        const updated = [...existing, entry];

        const config = vscode.workspace.getConfiguration('etlsql');
        await config.update('hosted.servers', updated, vscode.ConfigurationTarget.Global);
        return entry;
    }

    public async selectTarget(): Promise<void> {
        const servers = this.getSavedServers();
        const items: Array<vscode.QuickPickItem & { action: 'local' | 'server' | 'add' | 'disconnect'; server?: HostedServerEntry }> = [
            {
                label: '$(device-desktop) Local (Offline)',
                description: this.sessions.isHosted() ? 'Switch to local workstation execution' : 'Active',
                action: 'local'
            }
        ];

        const currentOrigin = this.sessions.getSession()?.server.origin;
        for (const server of servers) {
            const isCurrent = currentOrigin === server.origin;
            items.push({
                label: `$(cloud) ${server.label}`,
                description: server.origin,
                detail: isCurrent
                    ? `Connected as ${this.sessions.getSession()?.user.username} (${this.sessions.getSession()?.tenantId})`
                    : server.environment ? `Environment: ${server.environment}` : `Alias: ${server.alias}`,
                action: 'server',
                server
            });
        }

        items.push({
            label: '$(add) Add Hosted Server…',
            description: 'Save a trusted Portal origin in user settings',
            action: 'add'
        });

        if (this.sessions.isHosted()) {
            items.push({
                label: '$(debug-disconnect) Disconnect from Hosted Portal',
                description: 'Revoke this native session and return to Local (Offline)',
                action: 'disconnect'
            });
        }

        const picked = await vscode.window.showQuickPick(items, {
            title: 'ETL-SQL Execution Target',
            placeHolder: 'Choose Local (Offline) or a trusted hosted Portal'
        });
        if (!picked) {
            return;
        }

        if (picked.action === 'local' || picked.action === 'disconnect') {
            await this.disconnect();
            return;
        }

        const targetServer = picked.action === 'add'
            ? await this.addHostedServerInteractive()
            : picked.server;
        if (!targetServer) {
            return;
        }

        await this.connectToServer(targetServer);
    }

    public async connect(serverAlias?: string): Promise<void> {
        const servers = this.getSavedServers();
        if (typeof serverAlias === 'string') {
            const entry = servers.find(server => server.alias.toLowerCase() === serverAlias.trim().toLowerCase());
            if (!entry) {
                vscode.window.showWarningMessage('ETL-SQL: Select a hosted server saved in your user settings.');
                return;
            }
            await this.connectToServer(entry);
            return;
        }
        const config = vscode.workspace.getConfiguration('etlsql');
        const workspaceAlias = (config.get<string>('hosted.defaultServerAlias') || '').trim();

        if (workspaceAlias) {
            const matched = servers.find(s => s.alias.toLowerCase() === workspaceAlias.toLowerCase());
            if (!matched) {
                const choice = await vscode.window.showWarningMessage(
                    `Workspace references unknown hosted server alias '${workspaceAlias}'. Workspace settings cannot auto-trust URLs.`,
                    'Add Hosted Server…',
                    'Choose Existing Server',
                    'Cancel'
                );
                if (choice === 'Add Hosted Server…') {
                    const added = await this.addHostedServerInteractive();
                    if (added) {
                        await this.connectToServer(added);
                    }
                    return;
                }
                if (choice !== 'Choose Existing Server') {
                    return;
                }
            }
        }

        await this.selectTarget();
    }

    public async connectToServer(server: HostedServerEntry): Promise<boolean> {
        if (vscode.env.remoteName) {
            vscode.window.showWarningMessage(
                'ETL-SQL: Browser loopback sign-in is not supported in remote VS Code sessions. Local (Offline) execution remains available.'
            );
            return false;
        }

        try {
            const session = await this.sessions.signIn(server);
            vscode.window.showInformationMessage(
                `ETL-SQL: Connected to ${session.server.label} (${session.tenantId}) as ${session.user.username}.`
            );
            return true;
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            vscode.window.showErrorMessage(`ETL-SQL Hosted Sign-In failed: ${redactSecrets(msg, this.sessions.getActiveSecrets())}`);
            return false;
        }
    }

    public async disconnect(): Promise<void> {
        this._activeRun = null;
        void vscode.commands.executeCommand('setContext', 'etlsql.isRunning', false);
        await this.sessions.disconnect();
        this.refreshStatusBar();
    }

    public async disconnectOnShutdown(): Promise<void> {
        this._activeRun = null;
        this._pendingSubmissions.clear();
        await this.sessions.disconnect({ bestEffortTimeoutMs: 2_000 });
    }

    public handleRunSelectionUnavailable(): void {
        vscode.window.showInformationMessage(
            'ETL-SQL: Run Selection is unavailable for hosted execution. Hosted runs execute saved Portal pipeline revisions — use Run Script (F5) or switch to Local (Offline).'
        );
    }

    public async runHostedScript(editor: vscode.TextEditor): Promise<void> {
        if (this._activeRun || this._runningCommand) {
            vscode.window.showWarningMessage('ETL-SQL: A hosted pipeline run is already in progress. Stop it first or wait for completion.');
            return;
        }

        this._runningCommand = true;
        try {
            await this.runSelectedPipeline(editor);
        } catch (error) {
            if (error instanceof HostedSessionExpiredError) {
                await this.handleExpiredSession(undefined);
            } else {
                const message = error instanceof Error ? error.message : String(error);
                vscode.window.showErrorMessage(`ETL-SQL: ${redactSecrets(message, this.sessions.getActiveSecrets())}`);
            }
        } finally {
            this._runningCommand = false;
        }
    }

    public async runSavedPipeline(pipelineId: number, version: number, displayName: string): Promise<HostedSubmissionOutcome> {
        if (this._activeRun || this._runningCommand) {
            void vscode.window.showWarningMessage('ETL-SQL: A hosted pipeline run is already in progress. Stop it first or wait for completion.');
            return 'skipped';
        }

        const session = this.sessions.getSession();
        if (!session) {
            return 'skipped';
        }

        this._runningCommand = true;
        try {
            const sameSession = () => this.sessions.getSession()?.sessionId === session.sessionId
                && this.matchesCaller({ origin: session.server.origin, tenantId: session.tenantId, userId: session.user.id });
            if (!await this.prepareNewSubmission(sameSession) || !sameSession()) {
                return 'skipped';
            }

            this.renderRunHeader(displayName, session.server.origin, session.tenantId, session.user.username,
                `PIPELINE:${pipelineId}`, version);
            return await this.submitWithRecovery({
                idempotencyKey: this._randomUuid(),
                origin: session.server.origin,
                tenantId: session.tenantId,
                userId: session.user.id,
                username: session.user.username,
                pipelineId,
                version,
                jobName: `PIPELINE:${pipelineId}`,
                status: 'unconfirmed',
                createdAtMs: Date.now()
            });
        } finally {
            this._runningCommand = false;
        }
    }

    private async runSelectedPipeline(editor: vscode.TextEditor): Promise<void> {
        const target = this.sessions.getTarget();
        if (target.kind !== 'hosted') {
            return;
        }

        if (!this.sessions.isSignedIn()) {
            const signedIn = await this.connectToServer(target.server);
            if (!signedIn) {
                return;
            }
        }

        const session = this.sessions.getSession();
        if (!session) {
            return;
        }

        const sameSession = () => this.sessions.getSession()?.sessionId === session.sessionId
            && this.matchesCaller({ origin: session.server.origin, tenantId: session.tenantId, userId: session.user.id });
        if (!await this.prepareNewSubmission(sameSession) || !sameSession()) {
            return;
        }
        let pipelineId: number | undefined;
        let summary: StudioRunnablePipelineDto;
        try {
            pipelineId = await this.resolvePipelineId(editor.document, session.server.alias, sameSession);
            if (pipelineId === undefined || !sameSession()) {
                return;
            }
            summary = await this.client.getRunnableRevision(pipelineId);
        } catch (err) {
            if (err instanceof HostedSessionExpiredError) {
                await this.handleExpiredSession(undefined);
                return;
            }
            if (sameSession()) {
                const msg = err instanceof Error ? err.message : String(err);
                vscode.window.showErrorMessage(`ETL-SQL: ${msg}`);
            }
            return;
        }
        if (!sameSession()) {
            return;
        }

        let targetVersion = summary.version;

        // Dirty buffer handling: Save to Portal & Run (when allowed), Run Last Saved Revision, or Cancel.
        if (editor.document.isDirty) {
            const canSave = summary.canAuthor && session.scopes.includes('studio.save')
                && session.studioSession.capabilities.includes('ScriptSave');
            const choices = canSave
                ? ['Save to Portal & Run', 'Run Last Saved Revision', 'Cancel']
                : ['Run Last Saved Revision', 'Cancel'];

            const choice = await vscode.window.showWarningMessage(
                `'${summary.name}' has unsaved local edits. Hosted execution runs a saved Portal revision (current saved version: ${summary.version}).`,
                { modal: true },
                ...choices
            );

            if (!choice || choice === 'Cancel' || !sameSession()) {
                return;
            }

            if (choice === 'Save to Portal & Run') {
                const savedVersion = await this.saveLinkedPipeline(pipelineId, editor.document.getText(), summary.version, sameSession);
                if (savedVersion === undefined) {
                    // Conflict, refusal, or cancel — editor buffer is preserved untouched.
                    return;
                }
                targetVersion = savedVersion;
            }
        }

        if (!sameSession()) {
            return;
        }
        const idempotencyKey = this._randomUuid();
        const pending: PendingSubmissionRecord = {
            idempotencyKey,
            origin: session.server.origin,
            tenantId: session.tenantId,
            userId: session.user.id,
            username: session.user.username,
            pipelineId,
            version: targetVersion,
            jobName: `PIPELINE:${pipelineId}`,
            status: 'unconfirmed',
            createdAtMs: Date.now()
        };

        this.renderRunHeader(editor.document.uri.toString(), session.server.origin, session.tenantId, session.user.username, pending.jobName, targetVersion);
        await this.submitWithRecovery(pending);
    }

    private async prepareNewSubmission(sameSession: () => boolean): Promise<boolean> {
        const pending = this.getPendingSubmission();
        if (!pending) {
            return sameSession();
        }

        const action = await vscode.window.showWarningMessage(
            `Submission unconfirmed (request ${pending.idempotencyKey}) for ${pending.jobName}. Check recent runs before starting another.`,
            'Check Status Again',
            'Switch to Local (Offline)',
            'Dismiss & Start New Run'
        );
        if (!sameSession()) {
            return false;
        }

        if (action === 'Check Status Again') {
            await this.recoverPendingSubmission(pending);
        } else if (action === 'Switch to Local (Offline)') {
            await this.disconnect();
        } else if (action === 'Dismiss & Start New Run') {
            this.forgetSubmission(pending);
            return true;
        }
        return false;
    }

    public async stopHostedRun(): Promise<void> {
        const active = this._activeRun;
        if (!active || !this.matchesCaller(active)) {
            return;
        }

        try {
            const result = await this.client.cancelPipelineRun(active.pipelineId, active.runId);
            if (result.kind === 'accepted') {
                active.cancelRequested = true;
                this._postResults({
                    type: 'message',
                    level: 'warn',
                    text: `Cancellation requested for run ${active.runId} — waiting for Orchestrator`
                });
                return;
            }
            if (result.kind === 'already_finished') {
                this._postResults({
                    type: 'message',
                    level: 'info',
                    text: `Run ${active.runId} already finished before cancellation could be applied.`
                });
                return;
            }
            if (result.kind === 'denied') {
                this._postResults({
                    type: 'message',
                    level: 'err',
                    text: `Cancel refused for run ${active.runId}: ${result.refusal.error}`
                });
                vscode.window.showErrorMessage(`ETL-SQL: ${result.refusal.error}`);
                return;
            }
            vscode.window.showErrorMessage(`ETL-SQL: Could not cancel run ${active.runId}: ${result.refusal.error}`);
        } catch (err) {
            if (err instanceof HostedSessionExpiredError) {
                await this.handleExpiredSession(undefined);
                return;
            }
            const msg = err instanceof Error ? err.message : String(err);
            vscode.window.showErrorMessage(`ETL-SQL: Cancel failed for run ${active.runId}: ${msg}`);
        }
    }

    public async submitWithRecovery(pending: PendingSubmissionRecord): Promise<HostedSubmissionOutcome> {
        if (!this.matchesCaller(pending)) {
            return 'cancelled';
        }
        if (Date.now() - pending.createdAtMs >= 24 * 60 * 60 * 1_000) {
            vscode.window.showWarningMessage('The 24-hour recovery window has expired. Check recent runs before starting another run.');
            return 'refused';
        }
        this._pendingSubmissions.set(this.submissionScope(pending.origin, pending.tenantId, pending.userId), pending);
        try {
            const result = await this.client.submitPipelineRun(pending.pipelineId, pending.version, pending.idempotencyKey);
            if (!this.matchesCaller(pending)) {
                return 'cancelled';
            }
            if (result.kind === 'accepted') {
                this.forgetSubmission(pending);
                await this.attachAndPollRun(result.receipt, pending);
                return 'accepted';
            }

            if (result.status === 409 && result.refusal.code === 'submission_outcome_unknown') {
                pending.status = 'unknown';
                this._postResults({
                    type: 'message',
                    level: 'warn',
                    text: `Submission unconfirmed (request ${pending.idempotencyKey}): ${result.refusal.error}`
                });
                this._postResults({ type: 'status', status: 'error' });
                return await this.promptUnknownOutcome(pending);
            }

            if (result.status === 503
                && (result.refusal.code === 'orchestrator_not_connected' || result.refusal.code === 'orchestrator_unreachable')) {
                this._postResults({
                    type: 'message',
                    level: 'err',
                    text: `Hosted submission unavailable (${result.refusal.code}): ${result.refusal.error}`
                });
                this._postResults({ type: 'status', status: 'error' });
                const choice = await vscode.window.showErrorMessage(
                    result.refusal.error,
                    'Retry Hosted',
                    'Switch to Local (Offline)'
                );
                if (choice === 'Retry Hosted') {
                    return await this.submitWithRecovery(pending);
                } else if (choice === 'Switch to Local (Offline)') {
                    await this.disconnect();
                }
                return 'refused';
            }

            if (result.status >= 500) {
                return await this.recoverPendingSubmission(pending);
            }

            this.forgetSubmission(pending);
            this._postResults({
                type: 'message',
                level: 'err',
                text: `Hosted run refused (${result.refusal.code}): ${result.refusal.error}`
            });
            this._postResults({ type: 'done', exitCode: 1 });
            vscode.window.showErrorMessage(`ETL-SQL (${result.refusal.code}): ${result.refusal.error}`);
            return result.refusal.code === 'stale_revision' ? 'stale_revision' : 'refused';
        } catch (err) {
            if (err instanceof HostedSessionExpiredError) {
                this._postResults({
                    type: 'message',
                    level: 'warn',
                    text: `Hosted session expired while submission ${pending.idempotencyKey} was pending.`
                });
                this._postResults({ type: 'status', status: 'error' });
                return await this.handleExpiredSession(pending);
            }

            if (err instanceof HostedNetworkError) {
                // Lost response / dropped connection — never mint a new key; query by-request first.
                pending.status = 'unconfirmed';
                this._postResults({
                    type: 'message',
                    level: 'warn',
                    text: `Submission unconfirmed (request ${pending.idempotencyKey}) — checking Portal for accepted run…`
                });
                return await this.recoverPendingSubmission(pending);
            }

            const msg = err instanceof Error ? err.message : String(err);
            this._postResults({ type: 'message', level: 'err', text: `Hosted submission error: ${msg}` });
            this._postResults({ type: 'done', exitCode: 1 });
            vscode.window.showErrorMessage(`ETL-SQL: ${msg}`);
            return 'refused';
        }
    }

    public async recoverPendingSubmission(pending: PendingSubmissionRecord): Promise<HostedSubmissionOutcome> {
        if (!this.matchesCaller(pending)) {
            vscode.window.showWarningMessage(
                `Cannot resume recovery for request ${pending.idempotencyKey}: active session does not match original host, tenant, and user.`
            );
            return 'cancelled';
        }

        try {
            const lookup = await this.client.getSubmissionByRequest(pending.pipelineId, pending.idempotencyKey);
            if (!this.matchesCaller(pending)) {
                return 'cancelled';
            }
            if (lookup.kind === 'found') {
                if (lookup.receipt.version !== pending.version) {
                    this._postResults({ type: 'message', level: 'warn', text: 'Submission remains unconfirmed. The receipt names a different revision.' });
                    return 'refused';
                }
                this.forgetSubmission(pending);
                this._postResults({
                    type: 'message',
                    level: 'info',
                    text: `Recovered accepted run ${lookup.receipt.runId} for request ${pending.idempotencyKey}.`
                });
                await this.attachAndPollRun(lookup.receipt, pending);
                return 'recovered';
            }

            if (lookup.kind === 'unknown') {
                pending.status = 'unknown';
                this._postResults({
                    type: 'message',
                    level: 'warn',
                    text: `Submission unconfirmed (request ${pending.idempotencyKey}): ${lookup.refusal.error}`
                });
                this._postResults({ type: 'status', status: 'error' });
                return await this.promptUnknownOutcome(pending);
            }

            if (lookup.kind === 'not_found') {
                this._postResults({
                    type: 'message',
                    level: 'warn',
                    text: `Submission ${pending.idempotencyKey} has no visible Portal receipt.`
                });
                this._postResults({ type: 'status', status: 'error' });
                const choice = await vscode.window.showWarningMessage(
                    `Submission ${pending.idempotencyKey} has no visible Portal receipt. Retry with the original key?`,
                    'Retry Hosted',
                    'Switch to Local (Offline)',
                    'Cancel'
                );
                if (choice === 'Retry Hosted') {
                    return await this.submitWithRecovery(pending);
                } else if (choice === 'Switch to Local (Offline)') {
                    await this.disconnect();
                }
                return 'refused';
            }

            this._postResults({
                type: 'message',
                level: 'err',
                text: `Recovery refused (${lookup.refusal.code}): ${lookup.refusal.error}`
            });
            this._postResults({ type: 'done', exitCode: 1 });
            return 'refused';
        } catch (err) {
            if (err instanceof HostedSessionExpiredError) {
                return await this.handleExpiredSession(pending);
            }
            const msg = err instanceof Error ? err.message : String(err);
            this._postResults({
                type: 'message',
                level: 'warn',
                text: `Submission unconfirmed (request ${pending.idempotencyKey}): ${msg}`
            });
            return await this.promptUnknownOutcome(pending);
        }
    }

    private async promptUnknownOutcome(pending: PendingSubmissionRecord): Promise<HostedSubmissionOutcome> {
        const choice = await vscode.window.showWarningMessage(
            `Submission unconfirmed (request ${pending.idempotencyKey}) for ${pending.jobName}. Portal cannot confirm whether Orchestrator accepted it.`,
            'Check Status Again',
            'Switch to Local (Offline)',
            'Cancel'
        );
        if (choice === 'Check Status Again') {
            return await this.recoverPendingSubmission(pending);
        } else if (choice === 'Switch to Local (Offline)') {
            await this.disconnect();
        }
        return 'unconfirmed';
    }

    private async handleExpiredSession(pending: PendingSubmissionRecord | undefined): Promise<HostedSubmissionOutcome> {
        const target = this.sessions.getTarget();
        if (target.kind !== 'hosted') {
            return 'cancelled';
        }

        const choice = await vscode.window.showWarningMessage(
            'Your hosted Portal session expired or was revoked. Editor buffers are preserved.',
            'Sign In Again',
            'Switch to Local (Offline)',
            'Cancel'
        );

        if (choice === 'Switch to Local (Offline)') {
            await this.disconnect();
            return 'cancelled';
        }
        if (choice !== 'Sign In Again') {
            return 'unconfirmed';
        }

        const ok = await this.connectToServer(target.server);
        if (!ok || !pending) {
            return 'unconfirmed';
        }

        const resumed = this.sessions.getSession();
        if (!resumed
            || resumed.server.origin !== pending.origin
            || resumed.tenantId !== pending.tenantId
            || resumed.user.id !== pending.userId) {
            vscode.window.showWarningMessage(
                'Signed in as a different host, tenant, or account than the pending submission. Original request was not resumed automatically.'
            );
            return 'unconfirmed';
        }

        return await this.recoverPendingSubmission(pending);
    }

    private async saveLinkedPipeline(
        pipelineId: number,
        scriptText: string,
        expectedVersion: number,
        sameSession: () => boolean
    ): Promise<number | undefined> {
        let result = await this.client.savePipelineScript(pipelineId, scriptText, expectedVersion);

        if (result.kind === 'seal_password_required') {
            const sealPassword = await vscode.window.showInputBox({
                password: true,
                prompt: 'Master Password required to seal plaintext secrets before saving to Portal',
                placeHolder: 'Master Password'
            });
            if (!sealPassword || !sameSession()) {
                return undefined;
            }
            result = await this.client.savePipelineScript(pipelineId, scriptText, expectedVersion, sealPassword);
        }

        if (result.kind === 'saved') {
            this._log(`[Hosted] Saved pipeline #${pipelineId} to version ${result.response.version}.`);
            return result.response.version;
        }

        if (result.kind === 'conflict') {
            const versionNote = result.currentVersion !== undefined
                ? ` Server is now at version ${result.currentVersion}.`
                : '';
            vscode.window.showErrorMessage(
                `ETL-SQL Save Conflict: ${result.error}${versionNote} Your local editor buffer was preserved.`
            );
            return undefined;
        }

        vscode.window.showErrorMessage(`ETL-SQL: Could not save to Portal: ${result.error}`);
        return undefined;
    }

    private async resolvePipelineId(document: vscode.TextDocument, serverAlias: string, sameSession: () => boolean): Promise<number | undefined> {
        const config = vscode.workspace.getConfiguration('etlsql');
        const configuredAlias = config.get<string>('hosted.defaultServerAlias')?.trim();
        const useWorkspaceLinks = configuredAlias?.toLowerCase() === serverAlias.toLowerCase();
        const links = useWorkspaceLinks ? config.get<Record<string, number>>('hosted.pipelineLinks') ?? {} : {};
        const workspaceFolder = vscode.workspace.getWorkspaceFolder(document.uri);
        const relPath = workspaceFolder
            ? path.relative(workspaceFolder.uri.fsPath, document.fileName).replace(/\\/g, '/')
            : path.basename(document.fileName);

        const linkedId = links[relPath] ?? links[path.basename(document.fileName)];
        if (typeof linkedId === 'number' && linkedId > 0) {
            return linkedId;
        }

        const defaultId = useWorkspaceLinks ? config.get<number>('hosted.pipelineId') : undefined;
        if (typeof defaultId === 'number' && defaultId > 0) {
            return defaultId;
        }

        let query: string | undefined = path.basename(document.fileName, path.extname(document.fileName));
        let page = await this.client.findRunnablePipelines(query);
        if (sameSession() && page.items.length === 0 && page.nextAfterId === null && query) {
            query = undefined;
            page = await this.client.findRunnablePipelines();
        }
        while (sameSession()) {
            if (page.items.length === 0 && page.nextAfterId === null) {
                vscode.window.showWarningMessage('ETL-SQL: No more runnable Portal pipelines were found for this account.');
                return undefined;
            }

            const items: (vscode.QuickPickItem & { id?: number })[] = page.items.map(item => ({
                label: item.name,
                description: `v${item.version} ? ${item.folderPath}`,
                detail: `Pipeline #${item.id}`,
                id: item.id
            }));
            if (page.nextAfterId !== null) {
                items.push({ label: 'Show more pipelines' });
            }

            const picked = await vscode.window.showQuickPick(items, {
                title: `Select Saved Portal Pipeline to Run ? ${serverAlias}`,
                placeHolder: 'Choose a published pipeline revision'
            });
            if (!picked || !sameSession()) {
                return undefined;
            }
            if (picked.id !== undefined) {
                return picked.id;
            }
            if (page.nextAfterId !== null) {
                page = await this.client.findRunnablePipelines(query, page.nextAfterId);
            }
        }
        return undefined;
    }

    private async attachAndPollRun(
        receipt: PipelineRunSubmittedDto,
        pending: PendingSubmissionRecord
    ): Promise<void> {
        const active: ActiveHostedRun = {
            pipelineId: receipt.pipelineId,
            runId: receipt.runId,
            jobName: receipt.jobName,
            origin: pending.origin,
            tenantId: pending.tenantId,
            userId: pending.userId,
            username: pending.username,
            version: receipt.version,
            scriptHash: receipt.scriptHash,
            cancelRequested: false
        };
        this._activeRun = active;
        void vscode.commands.executeCommand('setContext', 'etlsql.isRunning', true);

        this._postResults({
            type: 'message',
            level: 'sys',
            text: `Accepted run #${receipt.runId} (${receipt.jobName}, v${receipt.version}${receipt.replayed ? ', replayed' : ''})`
        });

        let delayMs = this._initialPollDelayMs;
        let renderedStatementCount = 0;

        try {
            while (this._activeRun === active && this.matchesCaller(active)) {
                const polled = await this.client.getPipelineRun(active.pipelineId, active.runId);
                if (this._activeRun !== active || !this.matchesCaller(active)) {
                    return;
                }
                if (polled.kind === 'refused') {
                    this._postResults({
                        type: 'message',
                        level: 'err',
                        text: `Run #${active.runId} status check refused (${polled.refusal.code}): ${polled.refusal.error}`
                    });
                    this._postResults({ type: 'done', exitCode: 1 });
                    return;
                }

                const run = polled.run;
                renderedStatementCount = this.emitRunUpdate(run, renderedStatementCount);

                if (run.isTerminal) {
                    this.emitTerminalSummary(run);
                    return;
                }

                await this._sleep(delayMs);
                delayMs = Math.min(this._maxPollDelayMs, Math.round(delayMs * 1.5));
            }
        } catch (err) {
            if (err instanceof HostedSessionExpiredError) {
                await this.handleExpiredSession(undefined);
                return;
            }
            const msg = err instanceof Error ? err.message : String(err);
            this._postResults({
                type: 'message',
                level: 'err',
                text: `Polling run #${active.runId} failed: ${msg}`
            });
            this._postResults({ type: 'done', exitCode: 1 });
        } finally {
            if (this._activeRun === active) {
                this._activeRun = null;
                void vscode.commands.executeCommand('setContext', 'etlsql.isRunning', false);
            }
        }
    }

    private renderRunHeader(
        scriptUri: string,
        origin: string,
        tenantId: string,
        username: string,
        jobName: string,
        version: number
    ): void {
        this._postResults({ type: 'clear', scriptUri });
        this._postResults({ type: 'status', status: 'running' });
        this._postResults({
            type: 'message',
            level: 'sys',
            text: `Hosted Target: ${origin} | Tenant: ${tenantId} | User: ${username} | Job: ${jobName} (v${version})`
        });
    }

    private emitRunUpdate(run: PipelineRunDto, alreadyRendered: number): number {
        const statements = run.statements ?? [];
        for (let i = alreadyRendered; i < statements.length; i++) {
            const st = statements[i];
            const statusTag = st.failed ? 'FAILED' : 'OK';
            this._postResults({
                type: 'message',
                level: st.failed ? 'err' : 'info',
                text: `[${statusTag}] ${st.statement} (${st.durationMs} ms, ${st.rowsProcessed} rows)`
            });
        }

        if (statements.length > 0) {
            this._postResults({
                type: 'progress',
                data: statements.map((st, idx) => ({
                    id: `hosted-stmt-${idx + 1}`,
                    name: st.statement,
                    status: st.failed ? 'Faulted' : 'Completed',
                    rowsProcessed: st.rowsProcessed,
                    durationMs: st.durationMs
                }))
            });
        }

        return statements.length;
    }

    private emitTerminalSummary(run: PipelineRunDto): void {
        const totalMs = (run.statements ?? []).reduce((sum, st) => sum + st.durationMs, 0);
        this._postResults({
            type: 'performance',
            metrics: {
                executionMs: totalMs,
                rowsProcessed: run.rowsProcessed,
                memoryMb: 0,
                statements: (run.statements ?? []).map(st => ({
                    type: st.statement.split(/\s+/)[0] || 'STATEMENT',
                    totalMs: st.durationMs
                }))
            }
        });

        const level = run.status === 'SUCCESS' ? 'info' : run.status === 'CANCELLED' ? 'warn' : 'err';
        const errorSuffix = run.errorMessage ? ` — ${run.errorMessage}` : '';
        this._postResults({
            type: 'message',
            level,
            text: `Run #${run.runId} finished: ${run.status} (${run.rowsProcessed} rows)${errorSuffix}`
        });
        this._postResults({
            type: 'done',
            exitCode: run.status === 'SUCCESS' ? 0 : 1
        });
    }
}
