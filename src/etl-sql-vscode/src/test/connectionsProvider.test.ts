import { describe, it, expect, vi, beforeEach } from 'vitest';
import * as vscode from 'vscode';
import {
    ConnectionsProvider,
    ConnectionItem,
    CategoryItem,
    TableItem
} from '../connectionsProvider';

describe('ConnectionsProvider', () => {
    let provider: ConnectionsProvider;
    let mockContext: vscode.ExtensionContext;

    beforeEach(() => {
        vi.restoreAllMocks();
        mockContext = {} as any;
        provider = new ConnectionsProvider(mockContext);
    });

    describe('refresh', () => {
        it('should trigger onDidChangeTreeData event', () => {
            const fireSpy = vi.spyOn((provider as any)._onDidChangeTreeData, 'fire');
            provider.refresh();
            expect(fireSpy).toHaveBeenCalled();
        });
    });

    describe('updateScriptConnections', () => {
        it('should update document connections and fire change event', () => {
            const fireSpy = vi.spyOn((provider as any)._onDidChangeTreeData, 'fire');
            const conns = [
                { name: 'conn1', type: 'MSSQL', connectionString: '', isDocument: true },
                { name: 'conn2', type: 'Postgres', connectionString: '', isDocument: false }
            ];

            provider.updateScriptConnections('file:///test.etlsql', conns);

            expect(fireSpy).toHaveBeenCalled();
            // Verify getChildren retrieves it
            const childrenPromise = provider.getChildren();
            return childrenPromise.then(children => {
                expect(children).toHaveLength(1);
                expect(children[0]).toBeInstanceOf(ConnectionItem);
                expect((children[0] as ConnectionItem).name).toBe('conn1');
            });
        });

        it('should delete connections for URI if filtered length is 0', () => {
            const conns = [
                { name: 'conn1', type: 'MSSQL', connectionString: '', isDocument: false }
            ];
            provider.updateScriptConnections('file:///test.etlsql', conns);
            return provider.getChildren().then(children => {
                expect(children).toHaveLength(0);
            });
        });
    });

    describe('updateVariables', () => {
        it('should merge variables and prevent duplicate names', () => {
            provider.variables = [
                { name: '@var1', value: '1', type: 'INT' },
                { name: '@var2', value: 'abc', type: 'VARCHAR' }
            ];

            // Update with a new value for @var1, and a new variable @var3
            provider.updateVariables([
                { name: '@var1', value: '2', type: 'INT' },
                { name: '@var3', value: 'today', type: 'DATE' }
            ]);

            expect(provider.variables).toHaveLength(3);
            const var1 = provider.variables.find(v => v.name === '@var1');
            expect(var1?.value).toBe('2');

            const var3 = provider.variables.find(v => v.name === '@var3');
            expect(var3?.value).toBe('today');
        });
    });

    describe('getChildren', () => {
        it('should return script variables category when active editor has variables', async () => {
            // Mock active editor
            (vscode.window as any).activeTextEditor = {
                document: {
                    languageId: 'etlsql',
                    uri: { toString: () => 'file:///test.etlsql' }
                }
            };

            provider.variables = [{ name: '@myVar', value: 'val', type: 'string' }];
            const rootItems = await provider.getChildren();

            const variablesCategory = rootItems.find(
                item => item instanceof CategoryItem && item.category === 'Script Variables'
            );
            expect(variablesCategory).toBeDefined();

            // Clean up mock
            (vscode.window as any).activeTextEditor = undefined;
        });

        it('should resolve table list from LSP client sendRequest for category items', async () => {
            const mockClient = {
                sendRequest: vi.fn().mockResolvedValue({ tables: ['tableA', 'tableB'] })
            };
            provider.client = mockClient;

            const categoryItem = new CategoryItem('Tables', 'conn1', {}, vscode.TreeItemCollapsibleState.Collapsed, 'file:///test.etlsql');
            const items = await provider.getChildren(categoryItem);

            expect(mockClient.sendRequest).toHaveBeenCalledWith('etlsql/getTables', {
                connectionName: 'conn1',
                uri: 'file:///test.etlsql'
            });
            expect(items).toHaveLength(2);
            expect(items[0]).toBeInstanceOf(TableItem);
            expect((items[0] as TableItem).name).toBe('tableA');
        });

        it('should resolve columns list from LSP client sendRequest for TableItem', async () => {
            const mockClient = {
                sendRequest: vi.fn().mockResolvedValue({ columns: ['colA INT', 'colB VARCHAR'] })
            };
            provider.client = mockClient;

            const tableItem = new TableItem('my_table', 'conn1', vscode.TreeItemCollapsibleState.Collapsed, 'file:///test.etlsql');
            const items = await provider.getChildren(tableItem);

            expect(mockClient.sendRequest).toHaveBeenCalledWith('etlsql/getColumns', {
                connectionName: 'conn1',
                tableName: 'my_table',
                uri: 'file:///test.etlsql'
            });
            expect(items).toHaveLength(2);
            expect(items[0].label).toBe('colA INT');
        });

        it('should return error item if LSP call throws', async () => {
            const mockClient = {
                sendRequest: vi.fn().mockRejectedValue(new Error('LSP error'))
            };
            provider.client = mockClient;

            const tableItem = new TableItem('my_table', 'conn1', vscode.TreeItemCollapsibleState.Collapsed, 'file:///test.etlsql');
            const items = await provider.getChildren(tableItem);

            expect(items).toHaveLength(1);
            expect(items[0].label).toBe('Error loading columns');
        });
    });
});
