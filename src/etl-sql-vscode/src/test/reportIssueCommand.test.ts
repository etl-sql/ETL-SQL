import { describe, it, expect, vi, beforeEach } from 'vitest';
import * as vscode from 'vscode';
import { reportIssue } from '../reportIssueCommand';
import * as logger from '../logger';

describe('reportIssueCommand', () => {
    let mockContext: vscode.ExtensionContext;
    let mockConfig: any;

    beforeEach(() => {
        vi.restoreAllMocks();
        vi.clearAllMocks();
        logger.clearLogs();

        mockContext = {
            extension: {
                packageJSON: {
                    version: '0.12.0'
                }
            }
        } as any;

        mockConfig = {
            get: vi.fn().mockImplementation((key) => {
                if (key === 'portal.url') {
                    return 'http://localhost:5001';
                }
                if (key === 'ai.endpoint') {
                    return 'https://api.openai.com/v1';
                }
                return undefined;
            })
        };
        vi.spyOn(vscode.workspace, 'getConfiguration').mockReturnValue(mockConfig as any);
    });

    it('should compile diagnostics, redact secrets, write to clipboard, and prompt to open browser', async () => {
        // Log using real logger methods to verify redaction flows end-to-end
        logger.log('Extension starting up...', 'info');
        logger.log('Port base mismatch detected: connectionString="password=super-secret-123"', 'warn');

        const clipboardSpy = vi.spyOn(vscode.env.clipboard, 'writeText');
        const showInfoSpy = vi.spyOn(vscode.window, 'showInformationMessage').mockResolvedValue('Yes' as any);
        const openExtSpy = vi.spyOn(vscode.env, 'openExternal');

        await reportIssue(mockContext);

        // Verify clipboard markdown output
        expect(clipboardSpy).toHaveBeenCalled();
        const clipboardArg = clipboardSpy.mock.calls[0][0];

        // Should include system meta
        expect(clipboardArg).toContain('### Environment Details');
        expect(clipboardArg).toContain('0.12.0'); // Extension version
        expect(clipboardArg).toContain('http://localhost:5001'); // Portal URL override

        // Should redact password in output logs automatically
        expect(clipboardArg).toContain('Extension starting up...');
        expect(clipboardArg).toContain('connectionString="[REDACTED]"');
        expect(clipboardArg).not.toContain('super-secret-123');

        // Verify user interaction and redirect
        expect(showInfoSpy).toHaveBeenCalled();
        expect(openExtSpy).toHaveBeenCalledWith(expect.objectContaining({
            toString: expect.any(Function)
        }));
        expect(openExtSpy.mock.calls[0][0].toString()).toBe('https://github.com/etl-sql/ETL-SQL/issues/new');
    });

    it('should not open browser if user declines GitHub prompt', async () => {
        vi.spyOn(vscode.window, 'showInformationMessage').mockResolvedValue('No' as any);
        const openExtSpy = vi.spyOn(vscode.env, 'openExternal');

        await reportIssue(mockContext);

        expect(openExtSpy).not.toHaveBeenCalled();
    });
});
