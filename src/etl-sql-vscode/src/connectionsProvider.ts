import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';


export interface Connection {
    name: string;
    type: string;
    connectionString: string;
    isDocument?: boolean;
}

export class ConnectionsProvider implements vscode.TreeDataProvider<TreeItem> {
    private _onDidChangeTreeData: vscode.EventEmitter<TreeItem | undefined | void> = new vscode.EventEmitter<TreeItem | undefined | void>();
    readonly onDidChangeTreeData: vscode.Event<TreeItem | undefined | void> = this._onDidChangeTreeData.event;

    private scriptConnectionsByUri: Map<string, Connection[]> = new Map();
    public variables: { name: string; value: string; type: string }[] = [];
    private metadataCache: Map<string, string[]> = new Map();
    public client: unknown; // Set by extension.ts
    public outputChannel?: vscode.OutputChannel;

    constructor(private context: vscode.ExtensionContext) {
    }

    refresh(): void {
        this._onDidChangeTreeData.fire();
    }

    updateScriptConnections(uri: string, conns: Connection[]) {
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

    updateVariables(vars: { name: string; value: string; type: string }[]) {
        if (!Array.isArray(vars)) {
            return;
        }
        
        // Merge variables by name to prevent the "two variables only shows one" bug
        // This handles both snapshots and incremental updates from the engine.
        const currentMap = new Map(this.variables.map(v => [v.name, v]));
        vars.forEach(v => {
            if (v && v.name) {
                currentMap.set(v.name, v);
            }
        });
        
        this.variables = Array.from(currentMap.values());
        this._onDidChangeTreeData.fire();
    }

    clearVariables() {
        this.variables = [];
        this._onDidChangeTreeData.fire();
    }

    getConnections(): Connection[] {
        const unique = new Map<string, Connection>();
        for (const conns of this.scriptConnectionsByUri.values()) {
            for (const c of conns) {
                unique.set(c.name, c);
            }
        }
        return Array.from(unique.values());
    }

    getTreeItem(element: TreeItem): vscode.TreeItem {
        return element;
    }

    async getChildren(element?: TreeItem): Promise<TreeItem[]> {
        if (!element) {
            const allScriptConns: (Connection & { uri: string })[] = [];
            for (const [uri, conns] of this.scriptConnectionsByUri.entries()) {
                allScriptConns.push(...conns.map(c => ({ ...c, uri })));
            }

            const all: (ConnectionItem | CategoryItem)[] = [
                ...allScriptConns.map(c => new ConnectionItem(c.name, c.type, c, true, vscode.TreeItemCollapsibleState.Collapsed, c.uri))
            ];

            const activeEditor = vscode.window.activeTextEditor;
            if (activeEditor && (activeEditor.document.languageId === 'etlsql' || activeEditor.document.languageId === 'rptsql')) {
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
                        const response = await (this.client as LanguageClient).sendRequest('etlsql/getTempTables', { uri: element.uri }) as { tables: string[] };
                        return response.tables.map((t: string) => new TableItem(t, 'TEMP', vscode.TreeItemCollapsibleState.Collapsed, element.uri));
                    } catch {
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
                    const response = await (this.client as LanguageClient).sendRequest(method, {
                        connectionName: element.connectionName,
                        uri: element.uri
                    }) as { views?: string[], tables?: string[] };
                    
                    const list = element.category === 'Views' ? (response.views || []) : (response.tables || []);
                    return list.map((t: string) => new TableItem(t, element.connectionName, vscode.TreeItemCollapsibleState.Collapsed, element.uri));
                } catch {
                    return [new TreeItem("Error loading " + element.category.toLowerCase(), vscode.TreeItemCollapsibleState.None)];
                }
            }
            return [new TreeItem("LSP not ready...", vscode.TreeItemCollapsibleState.None)];
        }

        if (element instanceof TableItem) {
            if (this.client) {
                try {
                    this.outputChannel?.appendLine(`[ConnectionsProvider] Fetching columns for table: ${element.connectionName}.${element.label}`);
                    const response = await (this.client as LanguageClient).sendRequest('etlsql/getColumns', {
                        connectionName: element.connectionName,
                        tableName: element.label,
                        uri: element.uri
                    }) as { columns: string[] };
                    
                    if (!response || !response.columns) {
                        this.outputChannel?.appendLine(`[ConnectionsProvider] WARNING: getColumns returned empty response for ${element.label}`);
                        return [new TreeItem("No columns found", vscode.TreeItemCollapsibleState.None)];
                    }

                    this.outputChannel?.appendLine(`[ConnectionsProvider] Successfully loaded ${response.columns.length} columns for ${element.label}`);
                    return response.columns.map((c: string) => new TreeItem(c, vscode.TreeItemCollapsibleState.None, 'column'));
                } catch (e: unknown) {
                    const message = e instanceof Error ? e.message : String(e);
                    this.outputChannel?.appendLine(`[ConnectionsProvider] ERROR in getColumns: ${message}`);
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
        public readonly connection: Connection,
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
        public readonly connection: Connection | Record<string, unknown>,
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
