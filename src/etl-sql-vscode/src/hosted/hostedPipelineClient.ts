import {
    PipelineRunCancelRequestedDto,
    PipelineRunDto,
    PipelineRunRefusalDto,
    PipelineRunSubmittedDto,
    SaveDesignerResponse,
    StudioCatalogPageDto,
    StudioReportDto,
    redactSecrets
} from './hostedTypes';
import { HostedSessionManager } from './hostedSessionManager';

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

    public async getReportSummary(pipelineId: number): Promise<StudioReportDto> {
        const resp = await this.sessions.authorizedFetch(`/api/studio/reports/${pipelineId}/summary`, {
            method: 'GET'
        });
        if (!resp.ok) {
            const refusal = await this.readRefusal(resp);
            throw new Error(refusal.error || `Could not load pipeline #${pipelineId} (${resp.status}).`);
        }
        return await resp.json() as StudioReportDto;
    }

    public async searchCatalog(query?: string, afterId: number = 0): Promise<StudioCatalogPageDto> {
        const params = new URLSearchParams();
        if (query) {
            params.set('query', query);
        }
        if (afterId > 0) {
            params.set('afterId', String(afterId));
        }
        const qs = params.toString();
        const resp = await this.sessions.authorizedFetch(`/api/studio/catalog${qs ? `?${qs}` : ''}`, {
            method: 'GET'
        });
        if (!resp.ok) {
            const refusal = await this.readRefusal(resp);
            throw new Error(refusal.error || `Could not load Studio catalog (${resp.status}).`);
        }
        return await resp.json() as StudioCatalogPageDto;
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
            return { kind: 'accepted', receipt: await resp.json() as PipelineRunSubmittedDto };
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
            return { kind: 'found', receipt: await resp.json() as PipelineRunSubmittedDto };
        }

        const refusal = await this.readRefusal(resp);
        if (resp.status === 404) {
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
            ? parsed.code
            : typeof parsed?.error === 'string'
                ? parsed.error
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
