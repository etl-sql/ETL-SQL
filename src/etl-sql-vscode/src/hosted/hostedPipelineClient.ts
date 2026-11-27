import {
    HostedJobCancelResponseDto,
    HostedJobItemDto,
    HostedJobRecentRunDto,
    HostedJobRunDetailDto,
    HostedJobsPageDto,
    HostedJobTriggerResponseDto,
    PipelineRunCancelRequestedDto,
    PipelineRunDto,
    PipelineRunRefusalDto,
    PipelineRunSubmittedDto,
    SaveDesignerResponse,
    StudioRunnablePipelinePageDto,
    StudioRunnablePipelineDto,
    redactSecrets
} from './hostedTypes';
import { HostedNetworkError, HostedSessionManager } from './hostedSessionManager';

export type SavePipelineResult =
    | { kind: 'saved'; response: SaveDesignerResponse }
    | { kind: 'conflict'; error: string; currentVersion?: number }
    | { kind: 'seal_password_required'; error: string }
    | { kind: 'forbidden'; error: string }
    | { kind: 'error'; status: number; error: string };

export type SubmitRunResult =
    | { kind: 'accepted'; receipt: PipelineRunSubmittedDto }
    | { kind: 'refused'; status: number; refusal: PipelineRunRefusalDto };

export type LookupByRequestResult =
    | { kind: 'found'; receipt: PipelineRunSubmittedDto }
    | { kind: 'not_found'; refusal: PipelineRunRefusalDto }
    | { kind: 'unknown'; refusal: PipelineRunRefusalDto }
    | { kind: 'refused'; status: number; refusal: PipelineRunRefusalDto };

export type GetRunResult =
    | { kind: 'ok'; run: PipelineRunDto }
    | { kind: 'refused'; status: number; refusal: PipelineRunRefusalDto };

export type CancelRunResult =
    | { kind: 'accepted'; receipt: PipelineRunCancelRequestedDto }
    | { kind: 'already_finished'; refusal: PipelineRunRefusalDto }
    | { kind: 'denied'; refusal: PipelineRunRefusalDto }
    | { kind: 'refused'; status: number; refusal: PipelineRunRefusalDto };

/**
 * Typed HTTP client for the live Portal Studio & Pipeline Run endpoints used by VS Code
 * hosted execution (ST55 / ST56 Slice 3).
 */
export class HostedPipelineClient {
    constructor(private readonly sessions: HostedSessionManager) {}

    public async getRunnableRevision(pipelineId: number): Promise<StudioRunnablePipelineDto> {
        const resp = await this.sessions.authorizedFetch(`/api/studio/pipelines/${pipelineId}/revision`, {
            method: 'GET'
        });
        if (!resp.ok) {
            const refusal = await this.readRefusal(resp);
            throw new Error(refusal.error || `Could not load pipeline #${pipelineId} (${resp.status}).`);
        }
        const revision = this.readRunnablePipeline(await resp.json());
        if (revision.id !== pipelineId) {
            throw new HostedNetworkError('Portal returned a different pipeline revision.');
        }
        return revision;
    }

    public async findRunnablePipelines(query?: string, afterId: number = 0): Promise<StudioRunnablePipelinePageDto> {
        const params = new URLSearchParams();
        if (query) {
            params.set('query', query);
        }
        if (afterId > 0) {
            params.set('afterId', String(afterId));
        }
        const qs = params.toString();
        const resp = await this.sessions.authorizedFetch(`/api/studio/pipelines/runnable${qs ? `?${qs}` : ''}`, {
            method: 'GET'
        });
        if (!resp.ok) {
            const refusal = await this.readRefusal(resp);
            throw new Error(refusal.error || `Could not load runnable pipelines (${resp.status}).`);
        }
        const page = await this.readJsonSafe(resp);
        const cursor = page?.nextAfterId;
        if (!page || !Array.isArray(page.items) || page.items.length > 50
            || (cursor !== null && (typeof cursor !== 'number' || !Number.isSafeInteger(cursor) || cursor <= afterId))) {
            throw new HostedNetworkError('Portal returned an invalid runnable pipeline page.');
        }
        return { items: page.items.map(value => this.readRunnablePipeline(value)), nextAfterId: cursor };
    }

    private readRunnablePipeline(input: unknown): StudioRunnablePipelineDto {
        const value = input as Partial<StudioRunnablePipelineDto> | null;
        if (!value || typeof value.id !== 'number' || !Number.isSafeInteger(value.id) || value.id <= 0
            || typeof value.name !== 'string' || typeof value.folderPath !== 'string'
            || typeof value.version !== 'number' || !Number.isSafeInteger(value.version) || value.version <= 0
            || typeof value.canAuthor !== 'boolean') {
            throw new HostedNetworkError('Portal returned invalid runnable revision metadata.');
        }
        return { id: value.id, name: value.name, folderPath: value.folderPath, version: value.version, canAuthor: value.canAuthor };
    }

    public async savePipelineScript(
        pipelineId: number,
        scriptText: string,
        expectedVersion: number,
        sealPassword?: string
    ): Promise<SavePipelineResult> {
        const body: Record<string, unknown> = {
            reportId: pipelineId,
            scriptText
        };
        if (sealPassword) {
            body.sealPassword = sealPassword;
        }

        const resp = await this.sessions.authorizedFetch('/api/designer/save', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'If-Match': `"${expectedVersion}"`
            },
            body: JSON.stringify(body)
        });

        if (resp.ok) {
            return { kind: 'saved', response: await resp.json() as SaveDesignerResponse };
        }

        const parsed = await this.readJsonSafe(resp);
        const errorText = this.sanitize(
            typeof parsed?.error === 'string' ? parsed.error : `Save failed (${resp.status}).`
        );

        if (resp.status === 409) {
            const current = parsed?.current as { version?: unknown } | undefined;
            const currentVersion = typeof current?.version === 'number' ? current.version : undefined;
            return { kind: 'conflict', error: errorText, currentVersion };
        }
        if (resp.status === 422 && parsed?.code === 'SEAL_PASSWORD_REQUIRED') {
            return { kind: 'seal_password_required', error: errorText };
        }
        if (resp.status === 403) {
            return { kind: 'forbidden', error: errorText };
        }
        return { kind: 'error', status: resp.status, error: errorText };
    }

    public async submitPipelineRun(
        pipelineId: number,
        version: number,
        idempotencyKey: string
    ): Promise<SubmitRunResult> {
        const resp = await this.sessions.authorizedFetch(`/api/studio/pipelines/${pipelineId}/runs`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Idempotency-Key': idempotencyKey
            },
            body: JSON.stringify({ version })
        });

        if (resp.status === 202) {
            return { kind: 'accepted', receipt: await this.readReceipt(resp, pipelineId, version) };
        }

        return {
            kind: 'refused',
            status: resp.status,
            refusal: await this.readRefusal(resp)
        };
    }

    public async getSubmissionByRequest(
        pipelineId: number,
        idempotencyKey: string
    ): Promise<LookupByRequestResult> {
        const resp = await this.sessions.authorizedFetch(
            `/api/studio/pipelines/${pipelineId}/runs/by-request/${encodeURIComponent(idempotencyKey)}`,
            { method: 'GET' }
        );

        if (resp.status === 200) {
            return { kind: 'found', receipt: await this.readReceipt(resp, pipelineId) };
        }

        const refusal = await this.readRefusal(resp);
        if (resp.status === 404 && refusal.code === 'submission_not_found') {
            return { kind: 'not_found', refusal };
        }
        if (resp.status === 409 && refusal.code === 'submission_outcome_unknown') {
            return { kind: 'unknown', refusal };
        }
        return { kind: 'refused', status: resp.status, refusal };
    }

    public async getPipelineRun(pipelineId: number, runId: number): Promise<GetRunResult> {
        const resp = await this.sessions.authorizedFetch(
            `/api/studio/pipelines/${pipelineId}/runs/${runId}`,
            { method: 'GET' }
        );
        if (resp.ok) {
            return { kind: 'ok', run: await resp.json() as PipelineRunDto };
        }
        return {
            kind: 'refused',
            status: resp.status,
            refusal: await this.readRefusal(resp)
        };
    }

    public async cancelPipelineRun(pipelineId: number, runId: number): Promise<CancelRunResult> {
        const resp = await this.sessions.authorizedFetch(
            `/api/studio/pipelines/${pipelineId}/runs/${runId}/cancel`,
            { method: 'POST' }
        );
        if (resp.status === 202) {
            return { kind: 'accepted', receipt: await resp.json() as PipelineRunCancelRequestedDto };
        }

        const refusal = await this.readRefusal(resp);
        if (resp.status === 409 && refusal.code === 'run_already_finished') {
            return { kind: 'already_finished', refusal };
        }
        if (resp.status === 403 && refusal.code === 'cancel_denied') {
            return { kind: 'denied', refusal };
        }
        return { kind: 'refused', status: resp.status, refusal };
    }

    public async listHostedJobs(limit: number = 50, offset: number = 0): Promise<HostedJobsPageDto> {
        const params = new URLSearchParams({
            limit: String(Math.max(1, Math.min(100, limit))),
            offset: String(Math.max(0, offset))
        });
        const resp = await this.sessions.authorizedFetch(`/api/studio/hosted-jobs?${params.toString()}`, {
            method: 'GET'
        });
        if (!resp.ok) {
            const refusal = await this.readRefusal(resp);
            throw new Error(refusal.error || `Could not load hosted jobs (${resp.status}).`);
        }
        const raw = await this.readJsonSafe(resp);
        if (!raw || !Array.isArray(raw.items)) {
            throw new HostedNetworkError('Portal returned an invalid hosted jobs payload.');
        }
        return {
            items: raw.items as HostedJobItemDto[],
            total: typeof raw.total === 'number' ? raw.total : raw.items.length,
            offset: typeof raw.offset === 'number' ? raw.offset : offset,
            limit: typeof raw.limit === 'number' ? raw.limit : limit,
            hasMore: Boolean(raw.hasMore),
            orchestratorOnline: Boolean(raw.orchestratorOnline)
        };
    }

    public async listHostedJobRuns(jobName: string, limit: number = 10): Promise<HostedJobRecentRunDto[]> {
        const resp = await this.sessions.authorizedFetch(
            `/api/studio/hosted-jobs/${encodeURIComponent(jobName)}/runs?limit=${Math.max(1, Math.min(50, limit))}`,
            { method: 'GET' }
        );
        if (!resp.ok) {
            const refusal = await this.readRefusal(resp);
            throw new Error(refusal.error || `Could not load recent runs for ${jobName} (${resp.status}).`);
        }
        const parsed = await resp.json() as unknown;
        if (!Array.isArray(parsed)) {
            throw new HostedNetworkError('Portal returned an invalid recent runs payload.');
        }
        return parsed as HostedJobRecentRunDto[];
    }

    public async getHostedJobRunDetail(jobName: string, runId: number): Promise<HostedJobRunDetailDto> {
        const resp = await this.sessions.authorizedFetch(
            `/api/studio/hosted-jobs/${encodeURIComponent(jobName)}/runs/${runId}`,
            { method: 'GET' }
        );
        if (!resp.ok) {
            const refusal = await this.readRefusal(resp);
            throw new Error(refusal.error || `Could not load run #${runId} detail (${resp.status}).`);
        }
        const detail = await resp.json() as HostedJobRunDetailDto;
        if (!detail || detail.runId !== runId) {
            throw new HostedNetworkError(`Portal returned detail for a different run than #${runId}.`);
        }
        return detail;
    }

    public async triggerHostedJob(jobName: string): Promise<HostedJobTriggerResponseDto> {
        const resp = await this.sessions.authorizedFetch(
            `/api/studio/hosted-jobs/${encodeURIComponent(jobName)}/trigger`,
            { method: 'POST' }
        );
        if (resp.status === 202 || resp.ok) {
            return await resp.json() as HostedJobTriggerResponseDto;
        }
        const refusal = await this.readRefusal(resp);
        throw new Error(refusal.error || `Could not run job ${jobName} (${resp.status}).`);
    }

    public async cancelHostedJobRun(jobName: string, runId: number): Promise<HostedJobCancelResponseDto> {
        const resp = await this.sessions.authorizedFetch(
            `/api/studio/hosted-jobs/${encodeURIComponent(jobName)}/runs/${runId}/cancel`,
            { method: 'POST' }
        );
        if (resp.ok) {
            const receipt = await this.readJsonSafe(resp);
            if (!receipt || receipt.runId !== runId || typeof receipt.jobName !== 'string'
                || receipt.jobName.toLowerCase() !== jobName.toLowerCase() || typeof receipt.message !== 'string') {
                throw new HostedNetworkError(`Portal returned an invalid cancellation receipt for run #${runId}.`);
            }
            return { runId, jobName: receipt.jobName, message: receipt.message };
        }
        const refusal = await this.readRefusal(resp);
        throw new Error(refusal.error || `Could not stop run #${runId} (${resp.status}).`);
    }

    private async readReceipt(resp: Response, pipelineId: number, expectedVersion?: number): Promise<PipelineRunSubmittedDto> {
        const value = await this.readJsonSafe(resp);
        if (!value || typeof value.runId !== 'number' || !Number.isSafeInteger(value.runId) || value.runId <= 0
            || value.pipelineId !== pipelineId || typeof value.version !== 'number'
            || !Number.isSafeInteger(value.version) || value.version <= 0
            || (expectedVersion !== undefined && expectedVersion !== value.version)
            || value.jobName !== `PIPELINE:${pipelineId}` || typeof value.scriptHash !== 'string'
            || !value.scriptHash || typeof value.replayed !== 'boolean') {
            throw new HostedNetworkError('Portal returned an invalid run receipt.');
        }
        return {
            runId: value.runId, pipelineId, version: value.version,
            scriptHash: value.scriptHash, jobName: value.jobName, replayed: value.replayed
        };
    }

    private async readJsonSafe(resp: Response): Promise<Record<string, unknown> | null> {
        try {
            const text = await resp.text();
            if (!text) {
                return null;
            }
            const parsed = JSON.parse(text) as unknown;
            return parsed && typeof parsed === 'object' ? parsed as Record<string, unknown> : null;
        } catch {
            return null;
        }
    }

    private async readRefusal(resp: Response): Promise<PipelineRunRefusalDto> {
        const parsed = await this.readJsonSafe(resp);
        const code = typeof parsed?.code === 'string'
            ? this.sanitize(parsed.code)
            : typeof parsed?.error === 'string'
                ? this.sanitize(parsed.error)
                : `http_${resp.status}`;
        const error = this.sanitize(
            typeof parsed?.error === 'string'
                ? parsed.error
                : typeof parsed?.detail === 'string'
                    ? parsed.detail
                    : resp.statusText || `Request failed (${resp.status})`
        );
        const currentVersion = typeof parsed?.currentVersion === 'number' ? parsed.currentVersion : null;
        return { code, error, currentVersion };
    }

    private sanitize(text: string): string {
        return redactSecrets(text, this.sessions.getActiveSecrets());
    }
}
