export const NATIVE_CLIENT_ID = 'etlsql-vscode';
export const DEFAULT_NATIVE_SCOPES = ['studio.read', 'studio.run', 'studio.save'] as const;

export interface HostedServerEntry {
    alias: string;
    label: string;
    origin: string;
    environment?: string;
}

export type ExecutionTarget =
    | { kind: 'local' }
    | { kind: 'hosted'; server: HostedServerEntry };

export interface AuthProvidersResponse {
    local: boolean;
    oidcEnabled: boolean;
    oidcLoginUrl?: string | null;
}

export interface NativeClientUserDto {
    id: number;
    username: string;
    email?: string | null;
    roles: string[];
}

export interface NativeClientTokenResponse {
    token: string;
    refreshToken: string;
    expiresAt: string;
    sessionId: string;
    clientId: string;
    user: NativeClientUserDto;
    tenantId: string;
    audience: string;
    scopes: string[];
}

export interface StudioSessionDto {
    mode: string;
    capabilities: string[];
    sourceControlEnabled: boolean;
    draftRecovery: boolean;
    privateDrafts?: boolean;
    maxScriptCharacters?: number;
}

export interface StudioReportDto {
    id: number;
    folderId: number;
    folderPath: string;
    name: string;
    description?: string | null;
    updatedAt: string;
    version: number;
    kind?: string;
}

export interface StudioCatalogPageDto {
    items: StudioReportDto[];
    nextAfterId: number | null;
}

export interface PipelineRunSubmittedDto {
    runId: number;
    pipelineId: number;
    version: number;
    scriptHash: string;
    jobName: string;
    replayed?: boolean;
}

export interface PipelineRunRefusalDto {
    code: string;
    error: string;
    currentVersion?: number | null;
}

export interface PipelineRunStatementDto {
    statement: string;
    durationMs: number;
    rowsProcessed: number;
    failed: boolean;
}

export interface PipelineRunDto {
    runId: number;
    pipelineId: number;
    status: string;
    isTerminal: boolean;
    startTime: string;
    endTime?: string | null;
    errorMessage?: string | null;
    rowsProcessed: number;
    scriptHash?: string | null;
    logTruncated: boolean;
    statements: PipelineRunStatementDto[];
}

export interface PipelineRunCancelRequestedDto {
    runId: number;
    pipelineId: number;
}

export interface SaveDesignerResponse {
    version: number;
    sourceRevision?: string | null;
    scriptText?: string | null;
}

export interface HostedSessionState {
    server: HostedServerEntry;
    token: string;
    refreshToken: string;
    expiresAtMs: number;
    sessionId: string;
    clientId: string;
    user: NativeClientUserDto;
    tenantId: string;
    audience: string;
    scopes: string[];
    studioSession: StudioSessionDto;
}

export interface PendingSubmissionRecord {
    idempotencyKey: string;
    origin: string;
    tenantId: string;
    userId: number;
    username: string;
    pipelineId: number;
    version: number;
    jobName: string;
    status: 'unconfirmed' | 'unknown';
    createdAtMs: number;
}

export interface ActiveHostedRun {
    pipelineId: number;
    runId: number;
    jobName: string;
    origin: string;
    tenantId: string;
    userId: number;
    username: string;
    version: number;
    scriptHash: string;
    cancelRequested: boolean;
}

export type OriginValidationResult =
    | { ok: true; origin: string; host: string }
    | { ok: false; error: string };

/**
 * Validates and canonicalizes a hosted Portal server origin.
 * Enforces HTTPS for hosted servers; allows plain HTTP strictly for explicit
 * loopback development hosts (localhost, 127.0.0.1, [::1]).
 */
export function normalizeTrustedServerOrigin(raw: string): OriginValidationResult {
    const trimmed = raw.trim();
    if (!trimmed) {
        return { ok: false, error: 'Server URL is required.' };
    }

    let parsed: URL;
    try {
        parsed = new URL(trimmed);
    } catch {
        return { ok: false, error: 'Server URL must be a valid absolute URL (e.g. https://portal.example.com).' };
    }

    if (parsed.username || parsed.password) {
        return { ok: false, error: 'Server URL must not contain embedded credentials.' };
    }

    if (parsed.search || parsed.hash) {
        return { ok: false, error: 'Server URL must not contain a query string or fragment.' };
    }

    if (parsed.pathname && parsed.pathname !== '/') {
        return { ok: false, error: 'Server URL must be an origin without a path.' };
    }

    const protocol = parsed.protocol.toLowerCase();
    const hostname = parsed.hostname.toLowerCase();
    const isLoopback = hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '::1' || hostname === '[::1]';

    if (protocol === 'http:') {
        if (!isLoopback) {
            return {
                ok: false,
                error: 'Plain HTTP is refused for hosted servers; use HTTPS (HTTP is allowed only for explicit localhost / 127.0.0.1 development hosts).'
            };
        }
    } else if (protocol !== 'https:') {
        return { ok: false, error: 'Server URL must use https:// (or http:// for localhost / 127.0.0.1 development hosts).' };
    }

    return { ok: true, origin: parsed.origin, host: parsed.host };
}

export function parseSavedServers(raw: unknown): HostedServerEntry[] {
    if (!Array.isArray(raw)) {
        return [];
    }

    const entries: HostedServerEntry[] = [];
    const seenAliases = new Set<string>();

    for (const item of raw) {
        if (!item || typeof item !== 'object') {
            continue;
        }
        const record = item as Record<string, unknown>;
        const rawOrigin = typeof record.origin === 'string'
            ? record.origin
            : typeof record.url === 'string'
                ? record.url
                : '';
        const validated = normalizeTrustedServerOrigin(rawOrigin);
        if (!validated.ok) {
            continue;
        }

        const label = typeof record.label === 'string' && record.label.trim()
            ? record.label.trim()
            : validated.host;
        const alias = typeof record.alias === 'string' && record.alias.trim()
            ? record.alias.trim()
            : label.toLowerCase().replace(/[^a-z0-9._-]+/g, '-');
        const aliasKey = alias.toLowerCase();
        if (seenAliases.has(aliasKey)) {
            continue;
        }
        seenAliases.add(aliasKey);

        const environment = typeof record.environment === 'string' && record.environment.trim()
            ? record.environment.trim()
            : undefined;

        entries.push({
            alias,
            label,
            origin: validated.origin,
            environment
        });
    }

    return entries;
}

export function redactSecrets(text: string, secrets: readonly string[]): string {
    let result = text;
    for (const secret of secrets) {
        if (secret && secret.length >= 8) {
            result = result.split(secret).join('[REDACTED]');
        }
    }
    return result;
}
