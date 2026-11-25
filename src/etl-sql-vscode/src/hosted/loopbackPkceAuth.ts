import * as crypto from 'crypto';
import * as http from 'http';
import * as net from 'net';

export interface PkcePair {
    verifier: string;
    challenge: string;
}

export interface LoopbackCallbackResult {
    code: string;
    state: string;
}

export interface LoopbackCallbackListener {
    readonly redirectUri: string;
    readonly port: number;
    waitForCallback(): Promise<LoopbackCallbackResult>;
    cancel(reason?: string): void;
}

export class LoopbackAuthError extends Error {
    constructor(
        public readonly code: 'access_denied' | 'cancelled' | 'timeout' | 'invalid_callback',
        message: string
    ) {
        super(message);
        this.name = 'LoopbackAuthError';
    }
}

export function base64UrlEncode(buffer: Buffer): string {
    return buffer
        .toString('base64')
        .replace(/=+$/g, '')
        .replace(/\+/g, '-')
        .replace(/\//g, '_');
}

export function createPkcePair(): PkcePair {
    const verifier = base64UrlEncode(crypto.randomBytes(32));
    const challenge = base64UrlEncode(crypto.createHash('sha256').update(verifier, 'ascii').digest());
    return { verifier, challenge };
}

export function createRandomState(): string {
    return base64UrlEncode(crypto.randomBytes(24));
}

function timingSafeStringEqual(left: string, right: string): boolean {
    const a = Buffer.from(left, 'utf8');
    const b = Buffer.from(right, 'utf8');
    return a.length === b.length && crypto.timingSafeEqual(a, b);
}

const SUCCESS_HTML = '<!doctype html><html lang="en"><head><meta charset="utf-8">' +
    '<title>ETL-SQL Sign-In Complete</title></head><body>' +
    '<h1>Signed in to ETL-SQL</h1>' +
    '<p>You may close this tab and return to VS Code.</p></body></html>';

const DENIED_HTML = '<!doctype html><html lang="en"><head><meta charset="utf-8">' +
    '<title>ETL-SQL Sign-In Cancelled</title></head><body>' +
    '<h1>Sign-in was not completed</h1>' +
    '<p>You may close this tab and return to VS Code.</p></body></html>';

/**
 * Starts an ephemeral RFC 8252 loopback HTTP listener on 127.0.0.1:<ephemeral>/callback.
 * Guarantees the server closes on success, denial, cancellation, or timeout.
 */
export async function startLoopbackCallbackListener(
    expectedState: string,
    timeoutMs: number = 120_000
): Promise<LoopbackCallbackListener> {
    let settled = false;
    let resolvePromise!: (result: LoopbackCallbackResult) => void;
    let rejectPromise!: (err: Error) => void;

    const promise = new Promise<LoopbackCallbackResult>((resolve, reject) => {
        resolvePromise = resolve;
        rejectPromise = reject;
    });

    let timer: NodeJS.Timeout | undefined;
    const sockets = new Set<net.Socket>();

    const server = http.createServer((req, res) => {
        const reqUrl = req.url ?? '/';
        let parsed: URL;
        try {
            parsed = new URL(reqUrl, 'http://127.0.0.1');
        } catch {
            writeHtml(res, 400, DENIED_HTML);
            return;
        }

        if (req.method !== 'GET' || parsed.pathname !== '/callback') {
            res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'no-store' });
            res.end('Not found');
            return;
        }

        const state = parsed.searchParams.get('state') ?? '';
        if (!state || !timingSafeStringEqual(state, expectedState)) {
            writeHtml(res, 400, DENIED_HTML);
            finishError(new LoopbackAuthError('invalid_callback', 'Loopback sign-in callback state mismatch.'));
            return;
        }

        const error = parsed.searchParams.get('error');
        if (error) {
            writeHtml(res, 200, DENIED_HTML);
            const code = error === 'access_denied' ? 'access_denied' : 'invalid_callback';
            finishError(new LoopbackAuthError(code, `Browser sign-in did not complete (${error}).`));
            return;
        }

        const authCode = parsed.searchParams.get('code') ?? '';
        if (!authCode) {
            writeHtml(res, 400, DENIED_HTML);
            finishError(new LoopbackAuthError('invalid_callback', 'Loopback sign-in callback did not include an authorization code.'));
            return;
        }

        writeHtml(res, 200, SUCCESS_HTML);
        finishSuccess({ code: authCode, state });
    });

    server.on('connection', socket => {
        sockets.add(socket);
        socket.on('close', () => sockets.delete(socket));
    });

    const closeServer = () => {
        if (timer) {
            clearTimeout(timer);
            timer = undefined;
        }
        for (const socket of sockets) {
            try {
                socket.destroy();
            } catch {
                // ignore
            }
        }
        sockets.clear();
        try {
            server.close();
        } catch {
            // ignore
        }
    };

    const finishSuccess = (result: LoopbackCallbackResult) => {
        if (settled) {
            return;
        }
        settled = true;
        closeServer();
        resolvePromise(result);
    };

    const finishError = (err: Error) => {
        if (settled) {
            return;
        }
        settled = true;
        closeServer();
        rejectPromise(err);
    };

    await new Promise<void>((resolve, reject) => {
        server.once('error', reject);
        server.listen(0, '127.0.0.1', () => {
            server.off('error', reject);
            resolve();
        });
    });

    const address = server.address();
    if (!address || typeof address === 'string') {
        closeServer();
        throw new Error('Failed to bind ephemeral loopback callback port.');
    }

    const port = address.port;
    const redirectUri = `http://127.0.0.1:${port}/callback`;

    if (timeoutMs > 0) {
        timer = setTimeout(() => {
            finishError(new LoopbackAuthError('timeout', 'Browser sign-in timed out before completing.'));
        }, timeoutMs);
        timer.unref?.();
    }

    return {
        redirectUri,
        port,
        waitForCallback: () => promise,
        cancel: (reason?: string) => {
            finishError(new LoopbackAuthError('cancelled', reason ?? 'Browser sign-in was cancelled.'));
        }
    };
}

function writeHtml(res: http.ServerResponse, statusCode: number, html: string): void {
    res.writeHead(statusCode, {
        'Content-Type': 'text/html; charset=utf-8',
        'Cache-Control': 'no-store',
        'Pragma': 'no-cache',
        'Connection': 'close'
    });
    res.end(html);
}
