/**
 * Drives the real Portal native-client browser sign-in flow over loopback HTTP:
 *   GET /api/auth/client/authorize
 *   -> 302 /api/auth/oidc/login?native_tx=...
 *   -> 302 <mock IdP authorize>
 *   -> GET /api/auth/oidc/callback?code=...&state=...
 *   -> 302 /api/auth/client/consent?tx=...
 *   -> GET /api/auth/client/consent?tx=... (extracts CSRF token)
 *   -> POST /api/auth/client/consent (decision=approve)
 *   -> 302 http://127.0.0.1:<port>/callback?code=...&state=...
 *   -> GET http://127.0.0.1:<port>/callback?code=...&state=...
 */
export async function driveRealPortalOidcConsentFlow(
    authorizeUrl: string,
    options?: {
        oidcCallbackState?: string;
        onCapturedCode?: (code: string) => void;
    }
): Promise<boolean> {
    const origin = new URL(authorizeUrl).origin;
    const cookies = new Map<string, string>();
    const oidcState = options?.oidcCallbackState ?? 'acceptance-oidc-state';

    const storeCookies = (resp: Response) => {
        const rawHeaders = resp.headers as unknown as { getSetCookie?: () => string[] };
        const setCookies = typeof rawHeaders.getSetCookie === 'function'
            ? rawHeaders.getSetCookie()
            : (resp.headers.get('set-cookie') ? [resp.headers.get('set-cookie')!] : []);
        for (const header of setCookies) {
            const pair = header.split(';', 1)[0];
            const eq = pair.indexOf('=');
            if (eq > 0) {
                const name = pair.slice(0, eq).trim();
                const value = pair.slice(eq + 1).trim();
                if (!value) {
                    cookies.delete(name);
                } else {
                    cookies.set(name, value);
                }
            }
        }
    };

    const cookieHeader = () =>
        [...cookies.entries()].map(([k, v]) => `${k}=${v}`).join('; ');

    const portalFetch = async (url: string, init?: RequestInit) => {
        const headers = new Headers(init?.headers);
        const cookie = cookieHeader();
        if (cookie) {
            headers.set('Cookie', cookie);
        }
        const resp = await fetch(url, { ...init, headers, redirect: 'manual' });
        storeCookies(resp);
        return resp;
    };

    // 1. GET /api/auth/client/authorize -> 302 /api/auth/oidc/login?native_tx=...
    const step1 = await portalFetch(authorizeUrl, { method: 'GET' });
    if (step1.status !== 302) {
        throw new Error(`Expected 302 from /api/auth/client/authorize, got ${step1.status}`);
    }
    const oidcLoginLoc = new URL(step1.headers.get('location')!, origin).toString();

    // 2. GET /api/auth/oidc/login?native_tx=... -> 302 <IdP>
    const step2 = await portalFetch(oidcLoginLoc, { method: 'GET' });
    if (step2.status !== 302) {
        throw new Error(`Expected 302 from /api/auth/oidc/login, got ${step2.status}`);
    }

    // 3. Complete OIDC callback at Portal -> 302 /api/auth/client/consent?tx=...
    const step3 = await portalFetch(
        `${origin}/api/auth/oidc/callback?code=mock-idp-code&state=${encodeURIComponent(oidcState)}`,
        { method: 'GET' }
    );
    if (step3.status !== 302) {
        throw new Error(`Expected 302 from /api/auth/oidc/callback, got ${step3.status}`);
    }
    const consentUrl = new URL(step3.headers.get('location')!, origin);
    const tx = consentUrl.searchParams.get('tx');
    if (!tx) {
        throw new Error(`Expected tx query param in consent redirect: ${consentUrl.toString()}`);
    }

    // 4. GET /api/auth/client/consent?tx=... -> HTML form with CSRF token
    const step4 = await portalFetch(consentUrl.toString(), { method: 'GET' });
    if (step4.status !== 200) {
        throw new Error(`Expected 200 from /api/auth/client/consent, got ${step4.status}`);
    }
    const html = await step4.text();
    const match = html.match(/name="csrf_token"\s+value="([^"]+)"/);
    if (!match) {
        throw new Error('Could not extract csrf_token from Portal consent form.');
    }
    const csrfToken = match[1];

    // 5. POST /api/auth/client/consent (approve) -> 302 http://127.0.0.1:<port>/callback?code=...&state=...
    const form = new URLSearchParams({
        tx,
        csrf_token: csrfToken,
        decision: 'approve'
    });
    const step5 = await portalFetch(`${origin}/api/auth/client/consent`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/x-www-form-urlencoded',
            'Origin': origin
        },
        body: form.toString()
    });
    if (step5.status !== 302) {
        throw new Error(`Expected 302 from POST /api/auth/client/consent, got ${step5.status}: ${await step5.text()}`);
    }
    const loopbackCallbackUrl = step5.headers.get('location')!;
    const cbParsed = new URL(loopbackCallbackUrl);
    const capturedCode = cbParsed.searchParams.get('code');
    if (capturedCode) {
        options?.onCapturedCode?.(capturedCode);
    }

    // 6. Deliver callback to the VS Code ephemeral loopback listener
    const step6 = await fetch(loopbackCallbackUrl, { method: 'GET' });
    if (step6.status !== 200) {
        throw new Error(`Expected 200 from loopback callback listener, got ${step6.status}`);
    }
    return true;
}
