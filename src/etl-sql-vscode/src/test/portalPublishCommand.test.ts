import { describe, it, expect, vi, beforeEach } from 'vitest';
import * as vscode from 'vscode';
import * as http from 'http';
import * as https from 'https';
import { publishToPortal } from '../portalPublishCommand';

// Mock the fs module dynamically since publishToPortal imports it
vi.mock('fs', () => ({
    readFileSync: vi.fn().mockReturnValue(Buffer.from('SELECT 1;'))
}));

// Mock the http and https modules at the module boundary
vi.mock('http', () => ({
    request: vi.fn()
}));
vi.mock('https', () => ({
    request: vi.fn()
}));

describe('portalPublishCommand', () => {
    let mockContext: vscode.ExtensionContext;
    let mockConfig: any;
    let requestsMade: any[];
    let mockGlobalState: Map<string, any>;

    beforeEach(() => {
        vi.restoreAllMocks();
        requestsMade = [];
        mockGlobalState = new Map();

        const mockSecrets = new Map<string, string>();
        mockContext = {
            globalState: {
                get: vi.fn().mockImplementation((key) => mockGlobalState.get(key)),
                update: vi.fn().mockImplementation((key, val) => {
                    mockGlobalState.set(key, val);
                    return Promise.resolve();
                })
            },
            secrets: {
                get: vi.fn().mockImplementation((key) => Promise.resolve(mockSecrets.get(key))),
                store: vi.fn().mockImplementation((key, val) => {
                    mockSecrets.set(key, val);
                    return Promise.resolve();
                }),
                delete: vi.fn().mockImplementation((key) => {
                    mockSecrets.delete(key);
                    return Promise.resolve();
                })
            }
        } as any;

        mockConfig = {
            get: vi.fn().mockReturnValue('http://localhost:5001'),
            update: vi.fn().mockResolvedValue(undefined)
        };
        vi.spyOn(vscode.workspace, 'getConfiguration').mockReturnValue(mockConfig as any);
    });

    const setupMockRequests = (overrides: Record<string, { status: number, data: any }> = {}) => {
        const mockRequestImpl = (options: any, callback: any) => {
            requestsMade.push(options);
            const path = options.path;

            let status = 200;
            let responseData: any = {};

            if (path.includes('/api/auth/login')) {
                status = overrides.login?.status ?? 200;
                responseData = overrides.login?.data ?? { token: 'mock-token-123' };
            } else if (path.includes('/api/scripts/upload')) {
                status = overrides.upload?.status ?? 200;
                responseData = overrides.upload?.data ?? { path: '/srv/etl-sql/scripts/temp.etlsql' };
            } else if (path.includes('/api/folders')) {
                status = overrides.folders?.status ?? 200;
                responseData = overrides.folders?.data ?? [
                    { id: 1, name: 'Finance', children: [{ id: 2, name: 'Reports' }] }
                ];
            } else if (path.includes('/api/reports')) {
                status = overrides.reports?.status ?? 201;
                responseData = overrides.reports?.data ?? { id: 99 };
            }

            const mockRes = {
                statusCode: status,
                on: vi.fn().mockImplementation((event, cb) => {
                    if (event === 'data') {
                        cb(JSON.stringify(responseData));
                    }
                    if (event === 'end') {
                        cb();
                    }
                })
            };

            const mockReq = {
                on: vi.fn(),
                write: vi.fn(),
                end: vi.fn()
            };

            // Invoke response callback on next tick
            setTimeout(() => callback(mockRes), 0);

            return mockReq;
        };

        vi.mocked(http.request).mockImplementation(mockRequestImpl as any);
        vi.mocked(https.request).mockImplementation(mockRequestImpl as any);
    };

    it('should run the complete publishing sequence successfully', async () => {
        setupMockRequests();

        // Mock VS Code user prompts
        vi.spyOn(vscode.window, 'showInputBox').mockImplementation((options) => {
            if (options?.prompt?.includes('username')) {
                return Promise.resolve('admin');
            }
            if (options?.prompt?.includes('password')) {
                return Promise.resolve('pass');
            }
            if (options?.prompt?.includes('Report name')) {
                return Promise.resolve('Sales Target');
            }
            if (options?.prompt?.includes('Description')) {
                return Promise.resolve('A target script');
            }
            return Promise.resolve('');
        });

        vi.spyOn(vscode.window, 'showQuickPick').mockResolvedValue({
            label: '/Finance/Reports',
            folderId: 2
        } as any);

        const infoMsgSpy = vi.spyOn(vscode.window, 'showInformationMessage');

        await publishToPortal(mockContext, 'C:/tmp/test_report.rptsql');

        // Allow microtasks to settle (as network is mocked via setTimeouts)
        await new Promise(r => setTimeout(r, 50));

        // Verify flow results
        expect(infoMsgSpy).toHaveBeenCalledWith(expect.stringContaining('"Sales Target" published successfully.'));
        expect(requestsMade).toHaveLength(4); // Login -> Upload -> Folders -> Create Report
        expect(requestsMade[0].path).toBe('/api/auth/login');
        expect(requestsMade[1].path).toBe('/api/scripts/upload');
        expect(requestsMade[2].path).toBe('/api/folders');
        expect(requestsMade[3].path).toBe('/api/reports');
    });

    it('should abort if authentication user prompt is canceled', async () => {
        setupMockRequests();

        vi.spyOn(vscode.window, 'showInputBox').mockResolvedValue(undefined); // canceled
        const errorMsgSpy = vi.spyOn(vscode.window, 'showErrorMessage');

        await publishToPortal(mockContext, 'C:/tmp/test_report.rptsql');
        await new Promise(r => setTimeout(r, 10));

        expect(requestsMade).toHaveLength(0); // Authentication never attempted
        expect(errorMsgSpy).not.toHaveBeenCalled();
    });

    it('should display error if script upload fails', async () => {
        setupMockRequests({
            upload: { status: 500, data: { error: 'Disk full' } }
        });

        vi.spyOn(vscode.window, 'showInputBox').mockImplementation((options) => {
            if (options?.prompt?.includes('username')) {
                return Promise.resolve('admin');
            }
            if (options?.prompt?.includes('password')) {
                return Promise.resolve('pass');
            }
            return Promise.resolve('');
        });

        const errorMsgSpy = vi.spyOn(vscode.window, 'showErrorMessage');

        await publishToPortal(mockContext, 'C:/tmp/test_report.rptsql');
        await new Promise(r => setTimeout(r, 50));

        expect(errorMsgSpy).toHaveBeenCalledWith(expect.stringContaining('Upload failed: Disk full'));
        expect(requestsMade).toHaveLength(2); // Login -> Upload (failed)
    });
});
