/**
 * Tracks documents whose save was performed by the script save policy itself.
 *
 * Securing a script rewrites the buffer and saves it, which fires `onDidSaveTextDocument` again.
 * The policy conditions that triggered the prompt (`NO_SAVE_SENSITIVE`, `CONNECTION_ENCRYPTION`)
 * are still true on that second pass, so without a guard the user is prompted forever.
 *
 * The mark is consumed by the first save event that sees it, and expires on its own if that event
 * never arrives — a save that fails raises no event, and a permanently stuck mark would silently
 * suppress the policy for the rest of the session.
 */
const pending = new Map<string, ReturnType<typeof setTimeout>>();

/** Default time after which an unconsumed mark is dropped. */
export const SAVE_GUARD_TTL_MS = 5000;

/** Records that the next save of this document is one the policy triggered. */
export function markPolicySave(documentKey: string, ttlMs: number = SAVE_GUARD_TTL_MS): void {
    clearPolicySave(documentKey);
    const timer = setTimeout(() => pending.delete(documentKey), ttlMs);
    // Do not hold the process open just for the expiry timer.
    (timer as unknown as { unref?: () => void }).unref?.();
    pending.set(documentKey, timer);
}

/**
 * Returns true exactly once per mark, when the save event for a policy-triggered save arrives.
 * Callers use it to skip re-prompting for that save.
 */
export function consumePolicySave(documentKey: string): boolean {
    const timer = pending.get(documentKey);
    if (timer === undefined) {
        return false;
    }
    clearTimeout(timer);
    pending.delete(documentKey);
    return true;
}

/** Drops any mark for the document without reporting it as consumed. */
export function clearPolicySave(documentKey: string): void {
    const timer = pending.get(documentKey);
    if (timer !== undefined) {
        clearTimeout(timer);
        pending.delete(documentKey);
    }
}
