import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { markPolicySave, consumePolicySave, clearPolicySave } from '../saveGuard';

/**
 * Securing a script rewrites the buffer and saves it. That save re-fires the save handler while the
 * policy conditions are still true, so the guard is the only thing standing between the user and an
 * endless "apply the save policy?" prompt.
 */
describe('saveGuard', () => {
    const doc = 'file:///c%3A/tmp/pipeline.etlsql';

    beforeEach(() => {
        vi.useFakeTimers();
        clearPolicySave(doc);
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('reports a marked save so the policy prompt is skipped', () => {
        markPolicySave(doc);
        expect(consumePolicySave(doc)).toBe(true);
    });

    it('reports nothing for a save the user performed', () => {
        expect(consumePolicySave(doc)).toBe(false);
    });

    it('is consumed once, so the next ordinary save still prompts', () => {
        markPolicySave(doc);
        expect(consumePolicySave(doc)).toBe(true);
        expect(consumePolicySave(doc)).toBe(false);
    });

    it('tracks documents independently', () => {
        const other = 'file:///c%3A/tmp/other.etlsql';
        markPolicySave(doc);
        expect(consumePolicySave(other)).toBe(false);
        expect(consumePolicySave(doc)).toBe(true);
    });

    /**
     * A save that fails raises no save event, so the mark would never be consumed. Left in place it
     * would suppress the policy for that document for the rest of the session.
     */
    it('expires an unconsumed mark instead of suppressing the policy forever', () => {
        markPolicySave(doc, 5000);
        vi.advanceTimersByTime(5001);
        expect(consumePolicySave(doc)).toBe(false);
    });

    it('keeps the mark until it expires', () => {
        markPolicySave(doc, 5000);
        vi.advanceTimersByTime(4999);
        expect(consumePolicySave(doc)).toBe(true);
    });

    it('re-marking restarts the expiry rather than stacking timers', () => {
        markPolicySave(doc, 5000);
        vi.advanceTimersByTime(4000);
        markPolicySave(doc, 5000);
        vi.advanceTimersByTime(4000);
        expect(consumePolicySave(doc)).toBe(true);
    });
});
