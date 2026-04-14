import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';

export interface Connection {
    name: string;
    type: string;
    connectionString: string;
}

export class ConnectionsProvider implements vscode.TreeDataProvider<TreeItem> {
    private _onDidChangeTreeData: vscode.EventEmitter<TreeItem | undefined | void> = new vscode.EventEmitter<TreeItem | undefined | void>();
    readonly onDidChangeTreeData: vscode.Event<TreeItem | undefined | void> = this._onDidChangeTreeData.event;

    private connections: Connection[] = [];
    private scriptConnectionsByUri: Map<string, any[]> = new Map();
    private variables: any[] = [];
    private metadataCache: Map<string, string[]> = new Map();
    public client: any; // Will be set by extension.ts
    public outputChannel?: vscode.OutputChannel;

    constructor(private context: vscode.ExtensionContext) {
        this.loadConnections();
    }

    refresh(): void {
        this.loadConnections();
        this._onDidChangeTreeData.fire();
    }

    updateScriptConnections(uri: string, conns: any[]) {
        const filtered = conns.filter(c => c.isDocument);
        if (filtered.length === 0) {
            this.scriptConnectionsByUri.delete(uri);
        } else {
            this.scriptConnectionsByUri.set(uri, filtered);
        }
        this._onDidChangeTreeData.fire();
    }

    removeScriptConnections(uri: string) {
        this.scriptConnectionsByUri.delete(uri);
        this._onDidChangeTreeData.fire();
    }

    updateVariables(vars: any[]) {
        this.variables = vars;
        this._onDidChangeTreeData.fire();
    }

    clearVariables() {
        this.variables = [];
        this._onDidChangeTreeData.fire();
    }

    private loadConnections() {
        const stored = this.context.globalState.get<string>('etlsql.connections', '[]');
        try {
            this.connections = JSON.parse(stored);
        } catch (e) {
            this.connections = [];
        }
    }

    private saveConnections() {
        this.context.globalState.update('etlsql.connections', JSON.stringify(this.connections));
    }

    addConnection(conn: Connection) {
        this.connections.push(conn);
        this.saveConnections();
        this.refresh();
    }

    removeConnection(name: string) {
        this.connections = this.connections.filter(c => c.name !== name);
        this.saveConnections();
        this.refresh();
    }

    getConnections(): Connection[] {
        return this.connections;
    }

    getTreeItem(element: TreeItem): vscode.TreeItem {
        return element;
    }

    async getChildren(element?: TreeItem): Promise<TreeItem[]> {
        if (!element) {
            const allScriptConns: any[] = [];
            for (const [uri, conns] of this.scriptConnectionsByUri.entries()) {
                allScriptConns.push(...conns.map(c => ({ ...c, uri })));
            }

            const all: (ConnectionItem | CategoryItem)[] = [
                ...this.connections.map(c => new ConnectionItem(c.name, c.type, c, false, vscode.TreeItemCollapsibleState.Collapsed)),
                ...allScriptConns.map(c => new ConnectionItem(c.name, c.type, c, true, vscode.TreeItemCollapsibleState.Collapsed, c.uri))
            ];

            const activeEditor = vscode.window.activeTextEditor;
            if (activeEditor && activeEditor.document.languageId === 'etlsql') {
                all.push(new CategoryItem('Temporary Tables', 'TEMP', {}, vscode.TreeItemCollapsibleState.Collapsed, activeEditor.document.uri.toString()));
                if (this.variables.length > 0) {
                    all.push(new CategoryItem('Script Variables', 'VARIABLES', {}, vscode.TreeItemCollapsibleState.Collapsed));
                }
            }

            return all;
        }

        if (element instanceof ConnectionItem) {
            return [
                new CategoryItem('Tables', element.name, element.connection, vscode.TreeItemCollapsibleState.Collapsed, element.uri),
                new CategoryItem('Views', element.name, element.connection, vscode.TreeItemCollapsibleState.Collapsed, element.uri)
            ];
        }

        if (element instanceof CategoryItem) {
            if (element.category === 'Temporary Tables') {
                if (this.client) {
                    try {
                        const response = await this.client.sendRequest('etlsql/getTempTables', { uri: element.uri });
                        return (response as any).tables.map((t: string) => new TableItem(t, 'TEMP', vscode.TreeItemCollapsibleState.Collapsed, element.uri));
                    } catch (e) {
                        return [new TreeItem("Error loading temp tables", vscode.TreeItemCollapsibleState.None)];
                    }
                }
                return [new TreeItem("LSP not ready...", vscode.TreeItemCollapsibleState.None)];
            }

            if (element.category === 'Script Variables') {
                return this.variables.map(v => new VariableItem(v.name, v.value, v.type));
            }

            if (this.client) {
                try {
                    const method = element.category === 'Views' ? 'etlsql/getViews' : 'etlsql/getTables';
                    const response = await this.client.sendRequest(method, {
                        connectionName: element.connectionName,
                        uri: element.uri
                    });
                    
                    const list = element.category === 'Views' ? (response as any).views : (response as any).tables;
                    return list.map((t: string) => new TableItem(t, element.connectionName, vscode.TreeItemCollapsibleState.Collapsed, element.uri));
                } catch (e: any) {
                    return [new TreeItem("Error loading " + element.category.toLowerCase(), vscode.TreeItemCollapsibleState.None)];
                }
            }
            return [new TreeItem("LSP not ready...", vscode.TreeItemCollapsibleState.None)];
        }

        if (element instanceof TableItem) {
            if (this.client) {
                try {
                    this.outputChannel?.appendLine(`[ConnectionsProvider] Fetching columns for table: ${element.connectionName}.${element.label}`);
                    const response = await this.client.sendRequest('etlsql/getColumns', {
                        connectionName: element.connectionName,
                        tableName: element.label,
                        uri: element.uri
                    });
                    
                    if (!response || !response.columns) {
                        this.outputChannel?.appendLine(`[ConnectionsProvider] WARNING: getColumns returned empty response for ${element.label}`);
                        return [new TreeItem("No columns found", vscode.TreeItemCollapsibleState.None)];
                    }

                    this.outputChannel?.appendLine(`[ConnectionsProvider] Successfully loaded ${response.columns.length} columns for ${element.label}`);
                    return response.columns.map((c: string) => new TreeItem(c, vscode.TreeItemCollapsibleState.None, 'column'));
                } catch (e: any) {
                    this.outputChannel?.appendLine(`[ConnectionsProvider] ERROR in getColumns: ${e.message || e}`);
                    return [new TreeItem("Error loading columns", vscode.TreeItemCollapsibleState.None)];
                }
            }
        }

        return [];
    }
}

export class TreeItem extends vscode.TreeItem {
    constructor(
        public readonly label: string,
        public readonly collapsibleState: vscode.TreeItemCollapsibleState,
        public readonly contextValue?: string
    ) {
        super(label, collapsibleState);
        if (contextValue === 'column') {
            this.iconPath = new vscode.ThemeIcon('symbol-field');
        }
    }
}

export class ConnectionItem extends TreeItem {
    constructor(
        public readonly name: string,
        public readonly type: string,
        public readonly connection: any,
        public readonly isScript: boolean,
        public readonly collapsibleState: vscode.TreeItemCollapsibleState,
        public readonly uri?: string
    ) {
        super(isScript ? `${name} (Script)` : name, collapsibleState, isScript ? 'connection-script' : 'connection-global');
        this.tooltip = `${this.name} (${this.type})${isScript ? ' [Script]' : ''}`;
        this.description = this.type;
        this.iconPath = new vscode.ThemeIcon(isScript ? 'layers-dot' : 'database');
    }
}

export class CategoryItem extends TreeItem {
    constructor(
        public readonly category: string,
        public readonly connectionName: string,
        public readonly connection: any,
        public readonly collapsibleState: vscode.TreeItemCollapsibleState,
        public readonly uri?: string
    ) {
        super(category, collapsibleState, 'category');
        this.iconPath = new vscode.ThemeIcon(category === 'Tables' ? 'table' : 'clippy');
    }
}

export class TableItem extends TreeItem {
    constructor(
        public readonly name: string,
        public readonly connectionName: string,
        public readonly collapsibleState: vscode.TreeItemCollapsibleState,
        public readonly uri?: string
    ) {
        super(name, collapsibleState, 'table');
        this.iconPath = new vscode.ThemeIcon('table');
    }
}

export class VariableItem extends TreeItem {
    constructor(
        public readonly name: string,
        public readonly value: string,
        public readonly typeName: string
    ) {
        super(name, vscode.TreeItemCollapsibleState.None, 'variable');
        this.description = value;
        this.tooltip = `Type: ${typeName}\nValue: ${value}`;
        this.iconPath = new vscode.ThemeIcon('symbol-variable');
    }
}
