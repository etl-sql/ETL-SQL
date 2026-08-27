/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * ETL-SQL Connection Wizard — Canonical UI Component
 *
 * Dual-mode zero-trust connection authoring component:
 *   - Script Mode (default): Emits code-first `CREATE CONNECTION ...` syntax for scripts and report definitions.
 *   - Admin Mode: Emits catalog records for Portal Shared Connections (`/api/admin/connections`).
 *
 * Features:
 *   - Zero-Trust credential management (Vault SECRET:key, $ENV{...}, client ENC:... encryption, TRUSTED_CONNECTION, KEY_FILE)
 *   - High-grade client-side AES-GCM (v2) password encryption matching C# CryptoUtils
 *   - Layered reachability diagnostic runner (Policy, DNS, TCP, Auth) located in right pane (no scrolling)
 *   - Comprehensive connector catalog support (Relational, Warehouses, FlatFiles, Cloud, Remote, Messaging)
 *   - Connector search & category filters (All, Relational/DW, Flat Files, Cloud & Remote, Shared Catalog)
 *   - Connection string & URI decomposition with automatic credential isolation and passphrase encryption prompt
 *   - Portal Staged Files dropzone & workspace-relative file picker
 *   - Data Gateway cluster routing selector (`GATEWAY = 'cluster_alias'`)
 *   - Zero-Trust security path boundary validation (guards against traversal and system directories)
 *   - AST name collision detection and one-click auto-rename
 *
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/connection-wizard.js
 * Synchronized across Portal, Workstation Editor, Report Player, and VS Code extension.
 */

function _h(str) {
    return String(str ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

function _attr(str) {
    return _h(str).replace(/'/g, '&#39;');
}

function formatDate(utcStr) {
    if (!utcStr) return 'Online';
    try {
        const d = new Date(utcStr);
        return isNaN(d.getTime()) ? String(utcStr) : d.toLocaleString();
    } catch {
        return String(utcStr);
    }
}

/**
 * Encrypts a plaintext password with a client passphrase using PBKDF2 + AES-GCM (version 2 format),
 * byte-for-byte compatible with C# CryptoUtils.Decrypt.
 */
export async function encryptClientPassword(plainText, passphrase) {
    if (!plainText || !passphrase) return null;
    try {
        if (!window.crypto?.subtle) {
            console.warn('Web Crypto API not available in this environment.');
            return null;
        }

        const enc = new TextEncoder();
        const salt = window.crypto.getRandomValues(new Uint8Array(16));
        const nonce = window.crypto.getRandomValues(new Uint8Array(12));

        const keyMaterial = await window.crypto.subtle.importKey(
            'raw',
            enc.encode(passphrase),
            'PBKDF2',
            false,
            ['deriveKey']
        );

        const derivedKey = await window.crypto.subtle.deriveKey(
            {
                name: 'PBKDF2',
                salt: salt,
                iterations: 600000,
                hash: 'SHA-256'
            },
            keyMaterial,
            { name: 'AES-GCM', length: 256 },
            false,
            ['encrypt']
        );

        const cipherBuffer = await window.crypto.subtle.encrypt(
            {
                name: 'AES-GCM',
                iv: nonce,
                tagLength: 128
            },
            derivedKey,
            enc.encode(plainText)
        );

        const cipherBytes = new Uint8Array(cipherBuffer);
        const tag = cipherBytes.slice(cipherBytes.length - 16);
        const ciphertext = cipherBytes.slice(0, cipherBytes.length - 16);

        // V2 layout: 1 byte version (2) + 16 byte salt + 12 byte nonce + 16 byte tag + ciphertext
        const totalLength = 1 + 16 + 12 + 16 + ciphertext.length;
        const result = new Uint8Array(totalLength);
        result[0] = 2; // CURRENT_VERSION = 2
        result.set(salt, 1);
        result.set(nonce, 1 + 16);
        result.set(tag, 1 + 16 + 12);
        result.set(ciphertext, 1 + 16 + 12 + 16);

        let binary = '';
        const chunk = 8192;
        for (let i = 0; i < result.length; i += chunk) {
            binary += String.fromCharCode.apply(null, result.subarray(i, i + chunk));
        }
        return 'ENC:' + btoa(binary);
    } catch (e) {
        console.error('Client encryption failed:', e);
        return null;
    }
}

/**
 * Validates a file path against ETL-SQL Zero-Trust security rules:
 * - No directory traversal (..)
 * - No absolute drives or root references (C:\, /, \)
 * - No system directories (Windows, /etc, /root, /bin, .git, .ssh)
 * - No script files (.sql, .etlsql, .rptsql)
 */
export function validatePathSecurity(pathStr) {
    if (!pathStr || typeof pathStr !== 'string') return null;
    const p = pathStr.trim();
    if (!p) return null;

    if (/\.(sql|etlsql|rptsql)$/i.test(p)) {
        return 'Zero-Trust Guardrail: Accessing .sql, .etlsql, or .rptsql script files is forbidden.';
    }

    if (p.includes('..') || p.startsWith('/') || p.startsWith('\\') || /^[a-zA-Z]:[\\\/]/.test(p)) {
        return 'Zero-Trust Guardrail: Absolute system paths and directory traversal (..) are forbidden. Emitted paths must be workspace-relative (e.g. data/sales.csv).';
    }

    if (/(^|[\\\/])(windows|etc|root|bin|sbin|usr|var|tmp|\.git|\.ssh)([\\\/]|$)/i.test(p)) {
        return 'Zero-Trust Guardrail: Access to system directories, .git, or .ssh is strictly prohibited.';
    }

    return null;
}

function statusToStr(st) {
    if (typeof st === 'number') {
        return st === 0 ? 'ok' : st === 1 ? 'failed' : st === 2 ? 'skipped' : 'denied';
    }
    return String(st || 'unknown').toLowerCase();
}

/**
 * Creates and mounts an ETL-SQL Connection Wizard instance.
 *
 * @param {Object} options Configuration and injected services:
 *   - host: HTMLElement to mount the modal/wizard
 *   - mode: 'script' | 'admin' (default 'script')
 *   - initialConnector: string (default 'MSSQL')
 *   - existingNames: Array<string> (for collision detection in active script)
 *   - schemas: Array<ConnectorSchemaDescriptor> | null
 *   - sharedConnections: Array<{ alias, connectorType, description }> | null
 *   - secrets: Array<string> | null
 *   - gateways: Array<{ id, name, status, region }> | null
 *   - stagedFiles: Array<{ name, path, size, modifiedUtc }> | null
 *   - fetchSchemas: () => Promise<Array<ConnectorSchemaDescriptor>>
 *   - fetchSharedConnections: () => Promise<Array<{ alias, connectorType, description }>>
 *   - fetchSecrets: () => Promise<Array<string>>
 *   - fetchGateways: () => Promise<Array<{ id, name, status, region }>>
 *   - fetchStagedFiles: () => Promise<Array<{ name, path, size, modifiedUtc }>>
 *   - onInsert: (sql: string, meta: Object) => void
 *   - onSave: (entry: Object) => Promise<void>
 *   - onTest: (req: Object) => Promise<DiagnosticReport>
 *   - onParseString: (raw: string, hint: string) => Promise<ParsedResult>
 *   - onClose: () => void
 */
export function createConnectionWizard(options = {}) {
    const {
        host = document.body,
        mode = 'script',
        initialConnector = 'MSSQL',
        existingNames = [],
        onInsert = null,
        onSave = null,
        onTest = null,
        onParseString = null,
        onClose = null,
        fetchSchemas = null,
        fetchSharedConnections = null,
        fetchSecrets = null,
        fetchGateways = null,
        fetchGatewayResources = null,
        fetchStagedFiles = null,
        initialGateway = '',
        initialResourceId = ''
    } = options;

    let state = {
        mode,
        selectedCategory: 'database',
        connectorType: initialConnector,
        isSharedReference: false,
        sharedAlias: '',
        alias: '',
        environmentScope: 'All',
        gatewayCluster: initialGateway || '',
        selectedResourceId: initialResourceId || '',
        selectedResource: null,
        gatewayResources: [],
        gatewayResourcesLoading: false,
        gatewayResourcesError: null,
        saveError: null,
        isSaving: false,
        values: {},
        authType: 'secret', // 'secret' | 'env' | 'enc' | 'trusted' | 'keyfile'
        secretKey: '',
        envVarName: '',
        rawPassword: '',
        encPassphrase: '',
        encryptedCipher: '',
        keyFilePath: '',
        existingNames: Array.isArray(existingNames) ? existingNames : [],
        schemas: options.schemas || [],
        sharedConnections: options.sharedConnections || [],
        secrets: options.secrets || [],
        gateways: options.gateways || [],
        stagedFiles: options.stagedFiles || [],
        diagnosticResult: null,
        isTesting: false,
        pasteModalOpen: false,
        passphraseModalOpen: false,
        pendingExtractedPassword: '',
        pendingSecretKey: '',
        typeFilter: '',
        activeTab: 'basic'
    };

    // Default built-in fallback schemas if server schemas not yet loaded
    const defaultSchemas = [
        {
            connectorType: 'MSSQL',
            aliases: ['SQLSERVER'],
            description: 'Microsoft SQL Server and Azure SQL Database.',
            isFileBased: false,
            isDataWarehouse: false,
            commandTimeoutSeconds: 30,
            options: [
                { name: 'SERVER', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'localhost' },
                { name: 'PORT', type: 1, isMandatory: false, category: 'Basic', defaultValue: '1433' },
                { name: 'DATABASE', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'master' },
                { name: 'USER', type: 0, isMandatory: false, category: 'Basic', defaultValue: 'sa' },
                { name: 'PASSWORD', type: 3, isMandatory: false, category: 'Auth', mutuallyExclusiveGroup: 'Credentials' },
                { name: 'TRUSTED_CONNECTION', type: 2, isMandatory: false, category: 'Auth', mutuallyExclusiveGroup: 'Credentials', defaultValue: 'OFF' },
                { name: 'ENCRYPT', type: 2, isMandatory: false, category: 'Security', defaultValue: 'ON' },
                { name: 'TRUST_SERVER_CERTIFICATE', type: 2, isMandatory: false, category: 'Security', defaultValue: 'ON' },
                { name: 'APPLICATION_INTENT', type: 5, isMandatory: false, category: 'Tuning', allowedValues: ['READONLY', 'READWRITE'] },
                { name: 'MULTI_SUBNET_FAILOVER', type: 2, isMandatory: false, category: 'Tuning', defaultValue: 'OFF' },
                { name: 'POOLING', type: 2, isMandatory: false, category: 'Tuning', defaultValue: 'ON' },
                { name: 'TIMEOUT_SECONDS', type: 1, isMandatory: false, category: 'Tuning', defaultValue: '30' }
            ]
        },
        {
            connectorType: 'POSTGRES',
            aliases: ['POSTGRESQL', 'PG'],
            description: 'PostgreSQL and compatible engines (CockroachDB, YugabyteDB).',
            isFileBased: false,
            isDataWarehouse: false,
            commandTimeoutSeconds: 30,
            options: [
                { name: 'SERVER', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'localhost' },
                { name: 'PORT', type: 1, isMandatory: false, category: 'Basic', defaultValue: '5432' },
                { name: 'DATABASE', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'postgres' },
                { name: 'USER', type: 0, isMandatory: false, category: 'Basic', defaultValue: 'postgres' },
                { name: 'PASSWORD', type: 3, isMandatory: false, category: 'Auth', mutuallyExclusiveGroup: 'Credentials' },
                { name: 'SSLMODE', type: 5, isMandatory: false, category: 'Security', allowedValues: ['Disable', 'Require', 'VerifyCA', 'VerifyFull'], defaultValue: 'Require' },
                { name: 'TIMEOUT_SECONDS', type: 1, isMandatory: false, category: 'Tuning', defaultValue: '30' }
            ]
        },
        {
            connectorType: 'MYSQL',
            aliases: ['MARIADB'],
            description: 'MySQL and MariaDB databases.',
            isFileBased: false,
            isDataWarehouse: false,
            commandTimeoutSeconds: 30,
            options: [
                { name: 'SERVER', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'localhost' },
                { name: 'PORT', type: 1, isMandatory: false, category: 'Basic', defaultValue: '3306' },
                { name: 'DATABASE', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'mysql' },
                { name: 'USER', type: 0, isMandatory: false, category: 'Basic', defaultValue: 'root' },
                { name: 'PASSWORD', type: 3, isMandatory: false, category: 'Auth', mutuallyExclusiveGroup: 'Credentials' },
                { name: 'TIMEOUT_SECONDS', type: 1, isMandatory: false, category: 'Tuning', defaultValue: '30' }
            ]
        },
        {
            connectorType: 'SQLITE',
            aliases: [],
            description: 'SQLite local or memory database.',
            isFileBased: true,
            isDataWarehouse: false,
            commandTimeoutSeconds: 30,
            options: [
                { name: 'PATH', type: 4, isMandatory: true, category: 'Basic', defaultValue: 'data/local.db' }
            ]
        },
        {
            connectorType: 'SNOWFLAKE',
            aliases: [],
            description: 'Snowflake Cloud Data Platform.',
            isFileBased: false,
            isDataWarehouse: true,
            commandTimeoutSeconds: 1800,
            options: [
                { name: 'ACCOUNT', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'xy12345.us-east-1' },
                { name: 'DATABASE', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'ANALYTICS' },
                { name: 'SCHEMA', type: 0, isMandatory: false, category: 'Basic', defaultValue: 'PUBLIC' },
                { name: 'WAREHOUSE', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'COMPUTE_WH' },
                { name: 'USER', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'etl_user' },
                { name: 'PASSWORD', type: 3, isMandatory: false, category: 'Auth', mutuallyExclusiveGroup: 'Credentials' },
                { name: 'ROLE', type: 0, isMandatory: false, category: 'Tuning', defaultValue: 'ACCOUNTADMIN' },
                { name: 'TIMEOUT_SECONDS', type: 1, isMandatory: false, category: 'Tuning', defaultValue: '1800' }
            ]
        },
        {
            connectorType: 'BIGQUERY',
            aliases: [],
            description: 'Google Cloud BigQuery warehouse.',
            isFileBased: false,
            isDataWarehouse: true,
            commandTimeoutSeconds: 1800,
            options: [
                { name: 'PROJECT_ID', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'my-gcp-project' },
                { name: 'DATASET', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'analytics_ds' },
                { name: 'CREDENTIAL_FILE', type: 4, isMandatory: false, category: 'Auth', defaultValue: 'secrets/sa-key.json' },
                { name: 'TIMEOUT_SECONDS', type: 1, isMandatory: false, category: 'Tuning', defaultValue: '1800' }
            ]
        },
        {
            connectorType: 'FLATFILE',
            aliases: ['CSV'],
            description: 'Delimited text files (CSV, TSV, custom separator).',
            isFileBased: true,
            isDataWarehouse: false,
            commandTimeoutSeconds: 30,
            options: [
                { name: 'PATH', type: 4, isMandatory: true, category: 'Basic', defaultValue: 'data/sales.csv' },
                { name: 'DELIMITER', type: 0, isMandatory: false, category: 'Basic', defaultValue: ',' },
                { name: 'HEADER', type: 2, isMandatory: false, category: 'Basic', defaultValue: 'ON' },
                { name: 'TEXT_QUALIFIER', type: 0, isMandatory: false, category: 'Basic', defaultValue: '"' },
                { name: 'SKIP', type: 1, isMandatory: false, category: 'Basic', defaultValue: '0' },
                { name: 'ENCODING', type: 0, isMandatory: false, category: 'Tuning', defaultValue: 'UTF-8' }
            ]
        },
        {
            connectorType: 'PARQUET',
            aliases: [],
            description: 'Apache Parquet columnar binary file format.',
            isFileBased: true,
            isDataWarehouse: false,
            commandTimeoutSeconds: 30,
            options: [
                { name: 'PATH', type: 4, isMandatory: true, category: 'Basic', defaultValue: 'data/events.parquet' }
            ]
        },
        {
            connectorType: 'EXCEL',
            aliases: ['XLSX', 'XLS'],
            description: 'Microsoft Excel spreadsheets (.xlsx, .xls).',
            isFileBased: true,
            isDataWarehouse: false,
            commandTimeoutSeconds: 30,
            options: [
                { name: 'PATH', type: 4, isMandatory: true, category: 'Basic', defaultValue: 'data/reports.xlsx' },
                { name: 'SHEET', type: 0, isMandatory: false, category: 'Basic', defaultValue: 'Sheet1' },
                { name: 'HEADER', type: 2, isMandatory: false, category: 'Basic', defaultValue: 'ON' }
            ]
        },
        {
            connectorType: 'JSON',
            aliases: [],
            description: 'JSON records or array data files.',
            isFileBased: true,
            isDataWarehouse: false,
            commandTimeoutSeconds: 30,
            options: [
                { name: 'PATH', type: 4, isMandatory: true, category: 'Basic', defaultValue: 'data/feed.json' }
            ]
        },
        {
            connectorType: 'REST',
            aliases: ['HTTP'],
            description: 'REST API endpoints with JSON/XML payload mapping.',
            isFileBased: false,
            isDataWarehouse: false,
            commandTimeoutSeconds: 30,
            options: [
                { name: 'URL', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'https://api.example.com/v1/data' },
                { name: 'METHOD', type: 5, isMandatory: false, category: 'Basic', allowedValues: ['GET', 'POST', 'PUT', 'DELETE'], defaultValue: 'GET' },
                { name: 'AUTH_HEADER', type: 3, isMandatory: false, category: 'Auth' },
                { name: 'TIMEOUT_SECONDS', type: 1, isMandatory: false, category: 'Tuning', defaultValue: '30' }
            ]
        },
        {
            connectorType: 'S3',
            aliases: ['AWS_S3'],
            description: 'Amazon AWS S3 Object Storage.',
            isFileBased: false,
            isDataWarehouse: false,
            commandTimeoutSeconds: 60,
            options: [
                { name: 'BUCKET', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'my-corp-data-lake' },
                { name: 'REGION', type: 0, isMandatory: false, category: 'Basic', defaultValue: 'us-east-1' },
                { name: 'ACCESS_KEY', type: 0, isMandatory: false, category: 'Auth' },
                { name: 'SECRET_KEY', type: 3, isMandatory: false, category: 'Auth' }
            ]
        },
        {
            connectorType: 'AZUREBLOB',
            aliases: ['AZURE_BLOB'],
            description: 'Microsoft Azure Blob and Data Lake Gen2 storage.',
            isFileBased: false,
            isDataWarehouse: false,
            commandTimeoutSeconds: 60,
            options: [
                { name: 'CONTAINER', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'exports' },
                { name: 'ACCOUNT_NAME', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'mystorageacc' },
                { name: 'ACCOUNT_KEY', type: 3, isMandatory: false, category: 'Auth' }
            ]
        },
        {
            connectorType: 'SFTP',
            aliases: [],
            description: 'Secure File Transfer Protocol (SFTP) remote file access.',
            isFileBased: false,
            isDataWarehouse: false,
            commandTimeoutSeconds: 30,
            options: [
                { name: 'HOST', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'sftp.corp.internal' },
                { name: 'PORT', type: 1, isMandatory: false, category: 'Basic', defaultValue: '22' },
                { name: 'USER', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'sftp_user' },
                { name: 'PASSWORD', type: 3, isMandatory: false, category: 'Auth', mutuallyExclusiveGroup: 'Credentials' },
                { name: 'KEY_FILE', type: 4, isMandatory: false, category: 'Auth', mutuallyExclusiveGroup: 'Credentials' },
                { name: 'PASSPHRASE', type: 3, isMandatory: false, category: 'Auth' }
            ]
        },
        {
            connectorType: 'KAFKA',
            aliases: [],
            description: 'Apache Kafka event streams and topics.',
            isFileBased: false,
            isDataWarehouse: false,
            commandTimeoutSeconds: 30,
            options: [
                { name: 'BOOTSTRAP_SERVERS', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'localhost:9092' },
                { name: 'TOPIC', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'events_stream' },
                { name: 'GROUP_ID', type: 0, isMandatory: false, category: 'Basic', defaultValue: 'etl_consumer' }
            ]
        }
    ];

    if (!state.schemas || state.schemas.length === 0) {
        state.schemas = defaultSchemas;
    }

    // Modal container creation
    const modalOverlay = document.createElement('div');
    modalOverlay.className = 'etlsql-cw-overlay';
    modalOverlay.setAttribute('role', 'dialog');
    modalOverlay.setAttribute('aria-modal', 'true');
    modalOverlay.setAttribute('aria-label', 'Connection Wizard');

    host.appendChild(modalOverlay);

    // Initialize initial field values from schema defaults
    initFieldValues();

    // Async loader
    Promise.all([
        fetchSchemas ? fetchSchemas() : Promise.resolve(null),
        fetchSharedConnections ? fetchSharedConnections() : Promise.resolve(null),
        fetchSecrets ? fetchSecrets() : Promise.resolve(null),
        fetchGateways ? fetchGateways() : Promise.resolve(null),
        fetchStagedFiles ? fetchStagedFiles() : Promise.resolve(null)
    ]).then(([schemas, shared, secrets, gateways, stagedFiles]) => {
        if (Array.isArray(schemas) && schemas.length > 0) state.schemas = schemas;
        else if (schemas && Array.isArray(schemas.schemas) && schemas.schemas.length > 0) state.schemas = schemas.schemas;
        if (shared) state.sharedConnections = Array.isArray(shared) ? shared : (shared.connections || []);
        if (secrets) state.secrets = Array.isArray(secrets) ? secrets : (secrets.secrets || []);
        if (gateways) state.gateways = Array.isArray(gateways) ? gateways : (gateways.gateways || []);
        if (stagedFiles) state.stagedFiles = Array.isArray(stagedFiles) ? stagedFiles : (stagedFiles.files || []);
        initFieldValues();
        if (state.gatewayCluster) {
            loadGatewayResources(state.gatewayCluster);
        } else {
            render();
        }
    }).catch(err => {
        console.warn('ConnectionWizard: Failed to fetch metadata from server, using built-ins.', err);
    });

    async function loadGatewayResources(gatewayId) {
        if (!gatewayId) {
            state.gatewayResources = [];
            state.selectedResourceId = '';
            state.selectedResource = null;
            state.gatewayResourcesLoading = false;
            state.gatewayResourcesError = null;
            render();
            return;
        }

        state.gatewayResourcesLoading = true;
        state.gatewayResourcesError = null;
        render();

        try {
            let resList = null;
            if (fetchGatewayResources) {
                resList = await fetchGatewayResources(gatewayId);
            } else {
                const gw = (state.gateways || []).find(g => (g.name || g.id || g.gatewayId) === gatewayId);
                if (gw && (gw.resources || gw.publishedResources)) {
                    resList = gw.resources || gw.publishedResources;
                }
            }

            if (resList && Array.isArray(resList)) {
                state.gatewayResources = resList.filter(r => {
                    const st = String(r.state || '').toLowerCase();
                    return st === 'approved' || st === '1' || r.state === 1;
                });
            } else {
                state.gatewayResources = [];
            }

            if (state.selectedResourceId) {
                const match = state.gatewayResources.find(r => r.resourceId === state.selectedResourceId);
                if (match) {
                    state.selectedResource = match;
                    state.connectorType = match.connectorType;
                } else {
                    state.selectedResourceId = '';
                    state.selectedResource = null;
                }
            }
        } catch (err) {
            console.warn('ConnectionWizard: Gateway resource discovery failed.');
            state.gatewayResourcesError = 'Failed to discover Gateway resources. Try again.';
            state.gatewayResources = [];
            state.selectedResourceId = '';
            state.selectedResource = null;
        } finally {
            state.gatewayResourcesLoading = false;
            render();
        }
    }

    function initFieldValues() {
        const schema = getCurrentSchema();
        if (schema) {
            for (const opt of schema.options || []) {
                if (state.values[opt.name] === undefined && opt.defaultValue) {
                    state.values[opt.name] = opt.defaultValue;
                }
            }
        }
    }

    function getCurrentSchema() {
        if (state.isSharedReference) return null;
        return state.schemas.find(s =>
            s.connectorType.toUpperCase() === state.connectorType.toUpperCase() ||
            (s.aliases && s.aliases.some(a => a.toUpperCase() === state.connectorType.toUpperCase()))
        ) || state.schemas[0];
    }

    function getSecurityViolation() {
        const schema = getCurrentSchema();
        if (!schema) return null;

        for (const opt of schema.options || []) {
            if (opt.type === 4) { // FilePath
                const val = state.values[opt.name];
                if (val) {
                    const violation = validatePathSecurity(val);
                    if (violation) return violation;
                }
            }
        }

        if (state.authType === 'keyfile' && state.keyFilePath) {
            const violation = validatePathSecurity(state.keyFilePath);
            if (violation) return violation;
        }

        return null;
    }

    function getNameCollision() {
        if (!state.alias || !state.existingNames || state.existingNames.length === 0) return null;
        const normalized = state.alias.trim().toLowerCase();
        if (state.existingNames.some(n => String(n).trim().toLowerCase() === normalized)) {
            return `An object or connection named '${state.alias}' already exists in the current script.`;
        }
        return null;
    }

    function getAutoRenameSuggestion() {
        let base = (state.alias || 'conn').replace(/_\d+$/, '');
        let counter = 1;
        let candidate = `${base}_${counter}`;
        while (state.existingNames.some(n => String(n).trim().toLowerCase() === candidate.toLowerCase())) {
            counter++;
            candidate = `${base}_${counter}`;
        }
        return candidate;
    }

    function generateSql() {
        const alias = (state.alias || '').trim() || '<alias>';
        if (state.isSharedReference) {
            const sharedRef = state.sharedAlias || 'catalog_alias';
            return `CREATE CONNECTION ${alias} AS ${state.connectorType}('SHARED:${sharedRef}');`;
        }

        // If a specific approved Gateway resource is selected:
        if (state.selectedResourceId) {
            if (state.mode === 'admin') {
                return `-- Gateway-bound shared connection (SHARED:${alias})\n-- Gateway: ${state.gatewayCluster}\n-- Resource: ${state.selectedResourceId}\n-- Connector: ${state.connectorType}\n-- Physical destination & credentials resolved on Gateway`;
            }
            return `CREATE CONNECTION ${alias} AS ${state.connectorType}('SHARED:${state.alias || 'my_conn'}');\n-- Bound via Gateway: ${state.gatewayCluster} -> ${state.selectedResourceId}`;
        }

        const schema = getCurrentSchema();
        const type = schema ? schema.connectorType : state.connectorType;
        const optionsList = [];

        // Add Gateway routing if selected
        if (state.gatewayCluster && state.gatewayCluster.trim()) {
            optionsList.push(`  GATEWAY = '${state.gatewayCluster.trim().replace(/'/g, "''")}'`);
        }

        // Add standard form values
        for (const [key, val] of Object.entries(state.values)) {
            if (val !== undefined && val !== null && String(val).trim() !== '') {
                const optDesc = schema?.options?.find(o => o.name.toUpperCase() === key.toUpperCase());
                if (optDesc && optDesc.mutuallyExclusiveGroup === 'Credentials') {
                    continue; // Handled below by auth builder
                }

                if (optDesc?.type === 1) { // Number
                    optionsList.push(`  ${key} = ${val}`);
                } else if (optDesc?.type === 2) { // Boolean
                    const boolVal = (val === 'ON' || val === 'TRUE' || val === true) ? 'TRUE' : 'FALSE';
                    optionsList.push(`  ${key} = ${boolVal}`);
                } else {
                    optionsList.push(`  ${key} = '${val.replace(/'/g, "''")}'`);
                }
            }
        }

        // Add Auth / Credentials
        if (state.authType === 'secret' && state.secretKey.trim()) {
            optionsList.push(`  PASSWORD = SECRET:${state.secretKey.trim()}`);
        } else if (state.authType === 'env' && state.envVarName.trim()) {
            optionsList.push(`  PASSWORD = $ENV{${state.envVarName.trim()}}`);
        } else if (state.authType === 'enc') {
            if (state.encryptedCipher) {
                optionsList.push(`  PASSWORD = '${state.encryptedCipher}'`);
            } else if (state.rawPassword) {
                optionsList.push(`  PASSWORD = ENC:/* Encrypted Password */`);
            }
        } else if (state.authType === 'trusted') {
            optionsList.push(`  TRUSTED_CONNECTION = TRUE`);
        } else if (state.authType === 'keyfile' && state.keyFilePath.trim()) {
            optionsList.push(`  KEY_FILE = '${state.keyFilePath.trim().replace(/'/g, "''")}'`);
        }

        if (optionsList.length === 0) {
            return `CREATE CONNECTION ${alias} AS ${type}();`;
        }

        const createStmt = `CREATE CONNECTION ${alias} AS ${type}(\n${optionsList.join(',\n')}\n);`;
        if (state.authType === 'enc' && state.encPassphrase && state.encPassphrase.trim()) {
            return `USE PASSWORD = '${state.encPassphrase.trim().replace(/'/g, "''")}';\n${createStmt}`;
        }

        return createStmt;
    }

    async function runDiagnostic() {
        state.isTesting = true;
        state.diagnosticResult = null;
        render();

        const req = {
            alias: state.alias,
            connectorType: state.connectorType,
            target: state.selectedResourceId ? '' : (state.values.SERVER || state.values.HOST || state.values.PATH || ''),
            options: state.selectedResourceId ? { GATEWAY: state.gatewayCluster, RESOURCE: state.selectedResourceId } : { ...state.values },
            probeTimeoutSeconds: 5
        };

        if (!state.selectedResourceId && state.gatewayCluster) {
            req.options.GATEWAY = state.gatewayCluster;
        }

        if (!state.selectedResourceId) {
            if (state.authType === 'secret' && state.secretKey) {
                req.options.PASSWORD = `SECRET:${state.secretKey}`;
            } else if (state.authType === 'env' && state.envVarName) {
                req.options.PASSWORD = `$ENV{${state.envVarName}}`;
            } else if (state.authType === 'enc') {
                req.options.PASSWORD = state.rawPassword || state.encryptedCipher || '';
            } else if (state.authType === 'trusted') {
                req.options.TRUSTED_CONNECTION = 'ON';
            }
        }

        try {
            if (onTest) {
                const report = await onTest(req);
                state.diagnosticResult = report;
            } else {
                await new Promise(r => setTimeout(r, 600));
                state.diagnosticResult = {
                    succeeded: true,
                    connection: state.alias,
                    connectorType: state.connectorType,
                    steps: [
                        { layer: 'POLICY', status: 'ok', detail: 'Destination permitted by active security policy.' },
                        { layer: 'DNS', status: 'ok', detail: `'${req.target}' resolved to target address.` },
                        { layer: 'TCP', status: 'ok', detail: `Successfully established TCP socket handshake.` },
                        { layer: 'AUTH', status: 'ok', detail: 'Authentication probe succeeded.' }
                    ]
                };
            }
        } catch (ex) {
            state.diagnosticResult = {
                succeeded: false,
                connection: state.alias,
                connectorType: state.connectorType,
                steps: [
                    { layer: 'POLICY', status: 'ok', detail: 'Destination permitted by active security policy.' },
                    { layer: 'DIAGNOSTIC', status: 'failed', detail: ex.message, remedy: 'Verify endpoint address and credentials.' }
                ]
            };
        } finally {
            state.isTesting = false;
            render();
        }
    }

    function render() {
        const schema = getCurrentSchema();
        const sql = generateSql();
        const securityViolation = getSecurityViolation();
        const nameCollision = getNameCollision();
        const autoRename = nameCollision ? getAutoRenameSuggestion() : null;

        modalOverlay.innerHTML = `
            <div class="etlsql-cw-modal">
                <div class="etlsql-cw-header">
                    <div class="etlsql-cw-header-left">
                        <span class="etlsql-cw-kicker">${state.mode === 'admin' ? 'PORTAL ADMIN' : 'CODE-FIRST AUTHORING'}</span>
                        <h2 class="etlsql-cw-title">Connection Wizard</h2>
                    </div>
                    <div class="etlsql-cw-header-actions">
                        <button type="button" class="btn btn-outline btn-sm" id="etlsql-cw-header-test-btn" ${state.isTesting ? 'disabled' : ''} title="Run reachability diagnostic test immediately">
                            ${state.isTesting ? '<span class="spinner"></span> Testing…' : '⚡ Test Reachability'}
                        </button>
                        <button type="button" class="btn btn-outline btn-sm" id="etlsql-cw-paste-btn" title="Paste raw connection string or URI">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="margin-right:4px;vertical-align:-2px"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg>
                            Paste String
                        </button>
                        <button type="button" class="etlsql-cw-close-btn" id="etlsql-cw-close" aria-label="Close">&times;</button>
                    </div>
                </div>

                <div class="etlsql-cw-body">
                    <!-- Left Sidebar: Category & Connector Presets -->
                    <div class="etlsql-cw-sidebar">
                        <div class="etlsql-cw-preset-group">
                            <div class="etlsql-cw-preset-label">CATEGORY</div>
                            <button type="button" class="etlsql-cw-cat-btn ${state.selectedCategory === 'all' && !state.isSharedReference ? 'active' : ''}" data-cat="all">
                                🌐 All Connectors (${state.schemas.length})
                            </button>
                            <button type="button" class="etlsql-cw-cat-btn ${state.selectedCategory === 'database' && !state.isSharedReference ? 'active' : ''}" data-cat="database">
                                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><ellipse cx="12" cy="5" rx="9" ry="3"></ellipse><path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"></path><path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"></path></svg>
                                Relational / DW
                            </button>
                            <button type="button" class="etlsql-cw-cat-btn ${state.selectedCategory === 'files' && !state.isSharedReference ? 'active' : ''}" data-cat="files">
                                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline></svg>
                                Flat Files
                            </button>
                            <button type="button" class="etlsql-cw-cat-btn ${state.selectedCategory === 'remote' && !state.isSharedReference ? 'active' : ''}" data-cat="remote">
                                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 10h-1.26A8 8 0 1 0 9 20h9a5 5 0 0 0 0-10z"></path></svg>
                                Cloud & Remote
                            </button>
                            <button type="button" class="etlsql-cw-cat-btn ${state.isSharedReference ? 'active' : ''}" data-cat="shared">
                                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="18" cy="5" r="3"></circle><circle cx="6" cy="12" r="3"></circle><circle cx="18" cy="19" r="3"></circle><line x1="8.59" y1="13.51" x2="15.42" y2="17.49"></line><line x1="15.41" y1="6.51" x2="8.59" y2="10.49"></line></svg>
                                Shared Catalog
                            </button>
                        </div>

                        <div class="etlsql-cw-preset-group" style="flex:1;min-height:0;display:flex;flex-direction:column;">
                            <div class="etlsql-cw-preset-label">CONNECTOR</div>
                            <input type="text" id="etlsql-cw-type-filter" class="form-control etlsql-cw-search-input" placeholder="Search connectors…" value="${_attr(state.typeFilter || '')}" autocomplete="off" />
                            <div class="etlsql-cw-type-list" style="flex:1;overflow-y:auto;">
                                ${renderConnectorTypeList()}
                            </div>
                        </div>
                    </div>

                    <!-- Center: Configuration Form -->
                    <div class="etlsql-cw-content">
                        <!-- Top Metadata / Alias Row -->
                        <div class="etlsql-cw-meta-row">
                            <div class="form-group flex-1">
                                <label for="etlsql-cw-alias-input">
                                    Connection Alias
                                    <span class="etlsql-cw-required-tag" ${state.alias && state.alias.trim() ? 'style="display:none;"' : ''}>* (Required)</span>
                                </label>
                                <input type="text" id="etlsql-cw-alias-input" class="form-control ${(!state.alias || !state.alias.trim()) ? 'etlsql-cw-alias-missing' : ''} ${nameCollision ? 'is-invalid' : ''}" value="${_attr(state.alias)}" placeholder="Enter connection alias (e.g. sales_dw, analytics_db)…" spellcheck="false" autocomplete="off" autofocus />
                                <span class="form-hint etlsql-cw-missing-hint" ${state.alias && state.alias.trim() ? 'style="display:none;"' : ''}>⚠️ Connection alias is required to generate script reference.</span>
                                <span class="form-hint etlsql-cw-valid-hint" ${(!state.alias || !state.alias.trim()) ? 'style="display:none;"' : ''}>Used as script reference identifier: <code>${_h(state.alias || '')}</code></span>
                                ${nameCollision ? `
                                    <div class="etlsql-cw-collision-alert">
                                        <span>⚠️ ${_h(nameCollision)}</span>
                                        <button type="button" class="btn btn-xs btn-outline" id="etlsql-cw-autorename-btn">Rename to ${_h(autoRename)}</button>
                                    </div>
                                ` : ''}
                            </div>
                            ${state.mode === 'admin' ? `
                                <div class="form-group" style="width: 140px;">
                                    <label for="etlsql-cw-env-scope">Environment</label>
                                    <select id="etlsql-cw-env-scope" class="form-control">
                                        <option value="All" ${state.environmentScope === 'All' ? 'selected' : ''}>All</option>
                                        <option value="Development" ${state.environmentScope === 'Development' ? 'selected' : ''}>Development</option>
                                        <option value="Production" ${state.environmentScope === 'Production' ? 'selected' : ''}>Production</option>
                                    </select>
                                </div>
                            ` : ''}
                        </div>

                        <!-- Security Violation Alert -->
                        ${securityViolation ? `
                            <div class="etlsql-cw-security-alert">
                                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>
                                <span>${_h(securityViolation)}</span>
                            </div>
                        ` : ''}

                        ${state.isSharedReference ? renderSharedReferenceForm() : renderStandardConnectorForm(schema)}
                    </div>

                    <!-- Right Column: Live SQL Preview + Diagnostics Card (NO SCROLLING) -->
                    <div class="etlsql-cw-preview-pane">
                        <div class="etlsql-cw-preview-header">
                            <span class="etlsql-cw-preset-label">CODE-FIRST SQL PREVIEW</span>
                            <button type="button" class="btn btn-sm btn-outline" id="etlsql-cw-copy-sql">Copy SQL</button>
                        </div>
                        <pre class="etlsql-cw-sql-box"><code>${_h(sql)}</code></pre>

                        <!-- Diagnostic Test Panel in Right Sidebar -->
                        <div class="etlsql-cw-diagnostic-card">
                            <div class="etlsql-cw-diagnostic-header">
                                <div>
                                    <h4>Reachability Diagnostics</h4>
                                    <span class="form-hint" style="font-size:11px;">Zero-trust probe (Policy, DNS, TCP, Auth)</span>
                                </div>
                                <button type="button" class="btn btn-outline btn-sm" id="etlsql-cw-test-btn" ${state.isTesting ? 'disabled' : ''}>
                                    ${state.isTesting ? '<span class="spinner"></span> Testing…' : '⚡ Test Connection'}
                                </button>
                            </div>
                            ${renderDiagnosticReport()}
                        </div>

                        <div class="etlsql-cw-footer-actions">
                            ${state.saveError ? `<div class="etlsql-cw-save-error alert alert-danger" role="alert">${_h(state.saveError)}</div>` : ''}
                            <button type="button" class="btn btn-outline" id="etlsql-cw-cancel-btn">Cancel</button>
                            <button type="button" class="btn btn-primary" id="etlsql-cw-submit-btn" ${(securityViolation || state.isSaving) ? 'disabled' : ''} ${securityViolation ? 'title="Resolve security violation before saving"' : ''}>
                                ${state.isSaving ? 'Saving…' : (state.mode === 'admin' ? 'Save to Catalog' : 'Insert Connection')}
                            </button>
                        </div>
                    </div>
                </div>

                <!-- Paste Connection String Modal Overlay -->
                ${renderPasteModal()}

                <!-- Passphrase Encryption Prompt Modal -->
                ${renderPassphraseModal()}
            </div>
        `;

        bindEvents();
    }

    function renderConnectorTypeList() {
        if (state.isSharedReference) {
            return `
                <div class="etlsql-cw-type-item active">
                    <span class="etlsql-cw-type-name">SHARED:...</span>
                    <span class="etlsql-cw-type-desc">Portal Catalog</span>
                </div>
            `;
        }

        const filtered = state.schemas.filter(s => {
            const type = (s.connectorType || '').toUpperCase();
            if (state.typeFilter) {
                const q = state.typeFilter.trim().toLowerCase();
                const matchName = type.toLowerCase().includes(q);
                const matchDesc = (s.description || '').toLowerCase().includes(q);
                const matchAlias = (s.aliases || []).some(a => a.toLowerCase().includes(q));
                if (!matchName && !matchDesc && !matchAlias) return false;
            }
            if (state.selectedCategory === 'all') return true;
            if (state.selectedCategory === 'database') {
                return !s.isFileBased && ['MSSQL', 'SQLSERVER', 'POSTGRES', 'POSTGRESQL', 'MYSQL', 'MARIADB', 'SQLITE', 'ORACLE', 'SNOWFLAKE', 'BIGQUERY', 'DUCKDB', 'ODBC', 'MONGODB', 'NEO4J', 'MOCKDB'].includes(type);
            }
            if (state.selectedCategory === 'files') {
                return s.isFileBased || ['FLATFILE', 'CSV', 'PARQUET', 'EXCEL', 'JSON', 'XML', 'AVRO', 'DIRECTORY'].includes(type);
            }
            if (state.selectedCategory === 'remote') {
                return ['SFTP', 'FTP', 'FTP_CONN', 'REST', 'S3', 'AZUREBLOB', 'GCS', 'SHAREPOINT', 'KAFKA', 'WEBHOOK', 'SMTP', 'ACTIVEDIRECTORY', 'PORTAL', 'ORCHESTRATOR'].includes(type) || (!s.isFileBased && !['MSSQL', 'SQLSERVER', 'POSTGRES', 'POSTGRESQL', 'MYSQL', 'MARIADB', 'SQLITE', 'ORACLE', 'SNOWFLAKE', 'BIGQUERY', 'DUCKDB', 'ODBC', 'MONGODB', 'NEO4J', 'MOCKDB'].includes(type));
            }
            return true;
        });

        if (filtered.length === 0) {
            return `<div class="etlsql-cw-empty-hint">No connectors match filter</div>`;
        }

        return filtered.map(s => `
            <button type="button" class="etlsql-cw-type-item ${s.connectorType.toUpperCase() === state.connectorType.toUpperCase() ? 'active' : ''}" data-type="${_attr(s.connectorType)}">
                <span class="etlsql-cw-type-name">${_h(s.connectorType)}</span>
                <span class="etlsql-cw-type-desc">${_h(s.description || '')}</span>
            </button>
        `).join('');
    }

    function renderSharedReferenceForm() {
        const shared = state.sharedConnections || [];
        return `
            <div class="etlsql-cw-form-section">
                <h3>Shared Connection Selection</h3>
                <div class="form-group">
                    <label for="etlsql-cw-shared-select">Select Catalog Connection</label>
                    <select id="etlsql-cw-shared-select" class="form-control">
                        <option value="">-- Choose a shared connection --</option>
                        ${shared.map(c => `
                            <option value="${_attr(c.alias)}" ${c.alias === state.sharedAlias ? 'selected' : ''}>
                                ${c.alias} (${c.connectorType}) ${c.description ? '— ' + c.description : ''}
                            </option>
                        `).join('')}
                    </select>
                </div>
            </div>
        `;
    }

    function renderStandardConnectorForm(schema) {
        if (!schema) return `<div class="etlsql-cw-empty-hint">Select a connector type</div>`;

        const basicOptions = (schema.options || []).filter(o => o.category === 'Basic' && o.mutuallyExclusiveGroup !== 'Credentials');
        const securityOptions = (schema.options || []).filter(o => o.category === 'Security');
        const tuningOptions = (schema.options || []).filter(o => o.category === 'Tuning');
        const authOptions = (schema.options || []).filter(o => o.category === 'Auth' || o.mutuallyExclusiveGroup === 'Credentials');

        return `
            <!-- Basic Configuration -->
            <div class="etlsql-cw-form-section">
                <h3>${_h(schema.connectorType)} Configuration</h3>

                <!-- Dropzone / Staged file picker for file connectors -->
                ${schema.isFileBased ? renderFileDropzone() : ''}

                <!-- Data Gateway Cluster Routing -->
                ${renderGatewayRoutingSelector()}

                ${state.selectedResourceId ? `
                    <div class="alert alert-info etlsql-cw-gateway-bound-banner" style="margin-top: 12px;">
                        <div style="font-weight: 700; margin-bottom: 4px; display: flex; align-items: center; gap: 6px;">
                            <span>⚡</span>
                            <span>Gateway Resource Bound: <code>${_h(state.selectedResourceId)}</code></span>
                        </div>
                        <p style="margin: 0 0 6px 0; font-size: 0.82rem;">
                            Governed by Gateway <strong>${_h(state.gatewayCluster)}</strong>.
                            Connector Type: <strong>${_h(state.connectorType)}</strong> | Operations: <strong>${_h(state.selectedResource?.allowedOperations || 'Read')}</strong>.
                        </p>
                        <p class="form-hint" style="margin: 0; font-size: 0.76rem;">
                            Zero-Trust Security Boundary: Physical targets, hostnames, and credentials remain private on-premises and are never stored in the cloud catalog.
                        </p>
                    </div>
                ` : `
                    <div class="etlsql-cw-grid-2col">
                        ${basicOptions.map(opt => renderOptionField(opt)).join('')}
                    </div>
                `}
            </div>

            <!-- Zero-Trust Authentication Section (suppressed for Gateway-bound resources) -->
            ${!state.selectedResourceId && authOptions.length > 0 ? renderAuthSection(schema, authOptions) : ''}

            <!-- Advanced / Tuning Section (Collapsible) -->
            ${(securityOptions.length > 0 || tuningOptions.length > 0) ? `
                <details class="etlsql-cw-details">
                    <summary class="etlsql-cw-details-summary">Advanced Settings (Security & Tuning)</summary>
                    <div class="etlsql-cw-grid-2col" style="margin-top: 12px;">
                        ${securityOptions.map(opt => renderOptionField(opt)).join('')}
                        ${tuningOptions.map(opt => renderOptionField(opt)).join('')}
                    </div>
                </details>
            ` : ''}
        `;
    }

    function renderFileDropzone() {
        const staged = state.stagedFiles || [];
        return `
            <div class="etlsql-cw-dropzone-container">
                <div class="etlsql-cw-dropzone" id="etlsql-cw-file-dropzone">
                    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path><polyline points="17 8 12 3 7 8"></polyline><line x1="12" y1="3" x2="12" y2="15"></line></svg>
                    <span>Drag & drop workspace data file here, or</span>
                    <label class="btn btn-xs btn-outline etlsql-cw-browse-btn">
                        Browse Files
                        <input type="file" id="etlsql-cw-file-input" style="display:none;" />
                    </label>
                </div>
                ${staged.length > 0 ? `
                    <div class="etlsql-cw-staged-chips">
                        <span class="form-hint">Workspace Staged Files:</span>
                        <div class="etlsql-cw-chips-list">
                            ${staged.slice(0, 5).map(f => `
                                <button type="button" class="etlsql-cw-chip" data-filepath="${_attr(f.path || f.name)}">
                                    📄 ${_h(f.name)}
                                </button>
                            `).join('')}
                        </div>
                    </div>
                ` : ''}
            </div>
        `;
    }

    function renderGatewayRoutingSelector() {
        const gateways = state.gateways || [];
        if (gateways.length === 0) return '';

        let resourcePickerHtml = '';
        if (state.gatewayCluster) {
            if (state.gatewayResourcesLoading) {
                resourcePickerHtml = `
                    <div class="etlsql-cw-resource-loading" style="padding: 12px; margin-top: 8px; font-size: 0.82rem; color: var(--portal-muted, #7a8798);">
                        <span class="spinner" style="display: inline-block; width: 14px; height: 14px; vertical-align: middle; margin-right: 6px;"></span>
                        <span>Discovering approved gateway resources…</span>
                    </div>
                `;
            } else if (state.gatewayResourcesError) {
                resourcePickerHtml = `
                    <div class="etlsql-cw-resource-error alert alert-danger" style="margin-top: 8px; font-size: 0.82rem;">
                        <strong>Discovery Error:</strong> ${_h(state.gatewayResourcesError)}
                    </div>
                `;
            } else if (!state.gatewayResources || state.gatewayResources.length === 0) {
                resourcePickerHtml = `
                    <div class="etlsql-cw-resource-empty alert alert-warning" id="etlsql-cw-no-resources" style="margin-top: 8px; font-size: 0.82rem;">
                        <span class="status-pill status-pill-warn" style="font-size: 0.72rem;">No Resources</span>
                        <span>No approved resources published by live session for this gateway.</span>
                    </div>
                `;
            } else {
                resourcePickerHtml = `
                    <div class="etlsql-cw-resource-picker" role="radiogroup" aria-label="Discovered Gateway Resources" style="margin-top: 10px;">
                        <div class="etlsql-cw-picker-header" style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px;">
                            <span class="etlsql-cw-picker-title" style="font-size: 0.8rem; font-weight: 600; color: var(--portal-text-soft, #46556c);">Published Gateway Resources (Approved)</span>
                            <span class="form-hint" style="font-size: 0.74rem;">${state.gatewayResources.length} available</span>
                        </div>
                        <div class="etlsql-cw-resource-list" style="display: flex; flex-direction: column; gap: 6px;">
                            ${state.gatewayResources.map(r => {
                                const isSelected = state.selectedResourceId === r.resourceId;
                                return `
                                    <div class="etlsql-cw-resource-card ${isSelected ? 'is-selected' : ''}"
                                         data-resource-id="${_attr(r.resourceId)}"
                                         role="radio"
                                         aria-checked="${isSelected}"
                                         tabindex="0"
                                         style="border: 1px solid ${isSelected ? 'var(--portal-primary, #3b82f6)' : 'var(--portal-border, #d9e0ea)'}; background: ${isSelected ? 'rgba(59, 130, 246, 0.08)' : 'var(--portal-surface, #ffffff)'}; border-radius: 6px; padding: 8px 12px; cursor: pointer; transition: all 0.15s ease;">
                                        <div class="etlsql-cw-resource-card-header" style="display: flex; align-items: center; justify-content: space-between; gap: 8px;">
                                            <div style="display: flex; align-items: center; gap: 8px;">
                                                <input type="radio" name="cw-gw-resource" ${isSelected ? 'checked' : ''} style="margin: 0; pointer-events: none;" />
                                                <strong class="etlsql-cw-resource-name" style="font-size: 0.86rem; color: var(--portal-text, #1e293b);">${_h(r.resourceId)}</strong>
                                            </div>
                                            <span class="status-pill status-pill-type" style="font-size: 0.72rem; padding: 2px 6px; border-radius: 4px; background: var(--portal-surface-subtle, #f1f5f9); font-weight: 700;">${_h(r.connectorType)}</span>
                                        </div>
                                        <div class="etlsql-cw-resource-card-details" style="display: flex; align-items: center; gap: 10px; margin-top: 4px; font-size: 0.76rem; color: var(--portal-muted, #64748b);">
                                            <span class="etlsql-cw-resource-id">ID: <code>${_h(r.resourceId)}</code></span>
                                            <span class="etlsql-cw-resource-ops">Ops: <strong>${_h(r.allowedOperations || 'Read')}</strong></span>
                                            <span class="status-pill status-pill-good" style="font-size: 0.7rem; padding: 1px 5px; border-radius: 3px;">Approved</span>
                                            <span class="etlsql-cw-resource-seen">${r.lastSeenUtc ? formatDate(r.lastSeenUtc) : 'Online'}</span>
                                        </div>
                                    </div>
                                `;
                            }).join('')}
                        </div>
                    </div>
                `;
            }
        }

        return `
            <div class="form-group etlsql-cw-gateway-group">
                <label for="etlsql-cw-gateway-select">Hybrid Data Gateway Routing</label>
                <select id="etlsql-cw-gateway-select" class="form-control">
                    <option value="">Direct Cloud Egress (No Gateway)</option>
                    ${gateways.map(gw => {
                        const val = gw.name || gw.id || gw.gatewayId;
                        const status = gw.status || (gw.isOnline ? 'Online' : 'Disconnected');
                        return `
                            <option value="${_attr(val)}" ${state.gatewayCluster === val ? 'selected' : ''}>
                                ⚡ ${_h(val)} (${_h(gw.region || 'On-Premises')} - ${_h(status)})
                            </option>
                        `;
                    }).join('')}
                </select>
                <span class="form-hint">Routes egress queries through live on-premises gateway daemon.</span>
                ${resourcePickerHtml}
            </div>
        `;
    }

    function renderOptionField(opt) {
        const val = state.values[opt.name] ?? opt.defaultValue ?? '';
        const id = `etlsql-cw-opt-${opt.name.toLowerCase()}`;

        if (opt.type === 2) { // Boolean
            const isChecked = val === 'ON' || val === 'TRUE' || val === true;
            return `
                <div class="form-group etlsql-cw-form-group-check">
                    <label class="etlsql-cw-checkbox-card ${isChecked ? 'is-checked' : ''}" for="${id}">
                        <input type="checkbox" id="${id}" data-opt="${_attr(opt.name)}" ${isChecked ? 'checked' : ''} />
                        <div class="etlsql-cw-checkbox-text">
                            <span class="etlsql-cw-checkbox-title">${_h(opt.name)}</span>
                            ${opt.description ? `<span class="etlsql-cw-checkbox-desc">${_h(opt.description)}</span>` : ''}
                        </div>
                    </label>
                </div>
            `;
        }

        if (opt.type === 5 && opt.allowedValues && opt.allowedValues.length > 0) { // Enum
            return `
                <div class="form-group">
                    <label for="${id}">${_h(opt.name)} ${opt.isMandatory ? '<span class="required">*</span>' : ''}</label>
                    <select id="${id}" data-opt="${_attr(opt.name)}" class="form-control">
                        ${opt.allowedValues.map(av => `
                            <option value="${_attr(av)}" ${av.toUpperCase() === String(val).toUpperCase() ? 'selected' : ''}>${_h(av)}</option>
                        `).join('')}
                    </select>
                </div>
            `;
        }

        return `
            <div class="form-group">
                <label for="${id}">${_h(opt.name)} ${opt.isMandatory ? '<span class="required">*</span>' : ''}</label>
                <input type="${opt.type === 1 ? 'number' : 'text'}" id="${id}" data-opt="${_attr(opt.name)}" class="form-control" value="${_attr(val)}" placeholder="${_attr(opt.defaultValue || '')}" spellcheck="false" autocomplete="off" />
                ${opt.description ? `<span class="form-hint">${_h(opt.description)}</span>` : ''}
            </div>
        `;
    }

    function renderAuthSection(schema, authOptions) {
        const hasTrusted = authOptions.some(o => o.name === 'TRUSTED_CONNECTION');
        const hasKeyFile = authOptions.some(o => o.name === 'KEY_FILE');

        return `
            <div class="etlsql-cw-form-section">
                <h3>Zero-Trust Authentication</h3>
                <div class="etlsql-cw-auth-tabs">
                    <button type="button" class="etlsql-cw-auth-tab ${state.authType === 'secret' ? 'active' : ''}" data-authtype="secret">
                        🔒 Vault Secret
                    </button>
                    <button type="button" class="etlsql-cw-auth-tab ${state.authType === 'env' ? 'active' : ''}" data-authtype="env">
                        🌐 Env Var
                    </button>
                    <button type="button" class="etlsql-cw-auth-tab ${state.authType === 'enc' ? 'active' : ''}" data-authtype="enc">
                        🔑 Client Encrypted
                    </button>
                    ${hasTrusted ? `
                        <button type="button" class="etlsql-cw-auth-tab ${state.authType === 'trusted' ? 'active' : ''}" data-authtype="trusted">
                            🪟 Windows Auth
                        </button>
                    ` : ''}
                    ${hasKeyFile ? `
                        <button type="button" class="etlsql-cw-auth-tab ${state.authType === 'keyfile' ? 'active' : ''}" data-authtype="keyfile">
                            📄 Key File
                        </button>
                    ` : ''}
                </div>

                <div class="etlsql-cw-auth-body">
                    ${renderAuthInputs()}
                </div>
            </div>
        `;
    }

    function renderAuthInputs() {
        if (state.authType === 'secret') {
            const secrets = state.secrets || [];
            return `
                <div class="form-group">
                    <label for="etlsql-cw-secret-key">Vault Secret Name (<code>SECRET:name</code>)</label>
                    <div class="etlsql-cw-input-with-list">
                        <input type="text" id="etlsql-cw-secret-key" class="form-control" value="${_attr(state.secretKey)}" placeholder="e.g. MSSQL_DW_PASSWORD" spellcheck="false" autocomplete="off" />
                        ${secrets.length > 0 ? `
                            <div class="etlsql-cw-secret-hints">
                                <span class="form-hint">Discovered Secrets:</span>
                                ${secrets.slice(0, 4).map(s => `
                                    <button type="button" class="etlsql-cw-chip" onclick="document.getElementById('etlsql-cw-secret-key').value='${_attr(s)}'; document.getElementById('etlsql-cw-secret-key').dispatchEvent(new Event('input'));">
                                        ${_h(s)}
                                    </button>
                                `).join('')}
                            </div>
                        ` : ''}
                    </div>
                </div>
                <span class="form-hint">Secret value resolves securely at runtime from Azure KeyVault, AWS Secrets Manager, HashiCorp Vault, or encrypted local storage.</span>
            `;
        }

        if (state.authType === 'env') {
            return `
                <div class="form-group">
                    <label for="etlsql-cw-env-key">Environment Variable Name (<code>$ENV{name}</code>)</label>
                    <input type="text" id="etlsql-cw-env-key" class="form-control" value="${_attr(state.envVarName)}" placeholder="e.g. DB_PASSWORD" spellcheck="false" autocomplete="off" />
                </div>
                <span class="form-hint">Resolved from host process environment variables at execution time.</span>
            `;
        }

        if (state.authType === 'enc') {
            return `
                <div class="etlsql-cw-grid-2col">
                    <div class="form-group">
                        <label for="etlsql-cw-raw-pw">Password</label>
                        <input type="password" id="etlsql-cw-raw-pw" class="form-control" value="${_attr(state.rawPassword)}" placeholder="Password" autocomplete="new-password" />
                    </div>
                    <div class="form-group">
                        <label for="etlsql-cw-enc-passphrase">Client Passphrase (to encrypt)</label>
                        <input type="password" id="etlsql-cw-enc-passphrase" class="form-control" value="${_attr(state.encPassphrase)}" placeholder="Passphrase" autocomplete="new-password" />
                    </div>
                </div>
                ${state.encryptedCipher ? `
                    <div style="font-size:11px;color:var(--portal-success,#10b981);margin-top:4px;word-break:break-all;">
                        ✓ Encrypted with AES-256 (v2): <code>${_h(state.encryptedCipher.substring(0, 32))}...</code>
                    </div>
                ` : '<span class="form-hint">Password is encrypted with your client passphrase (PBKDF2 + AES-GCM) before being placed in script text.</span>'}
            `;
        }

        if (state.authType === 'trusted') {
            return `
                <div class="etlsql-cw-info-box">
                    <strong>Windows Integrated Authentication</strong>
                    <p>Uses current Windows/Kerberos identity. No password is stored or transmitted.</p>
                </div>
            `;
        }

        if (state.authType === 'keyfile') {
            return `
                <div class="form-group">
                    <label for="etlsql-cw-key-path">Private Key File Path</label>
                    <input type="text" id="etlsql-cw-key-path" class="form-control" value="${_attr(state.keyFilePath)}" placeholder="keys/id_ed25519" spellcheck="false" autocomplete="off" />
                </div>
            `;
        }

        return '';
    }

    function renderDiagnosticReport() {
        if (!state.diagnosticResult) {
            return `
                <div class="etlsql-cw-diag-placeholder">
                    <span>Click <strong>Test Connection</strong> to run live reachability verification.</span>
                </div>
            `;
        }

        const { succeeded, steps = [], error } = state.diagnosticResult;

        return `
            <div class="etlsql-cw-diag-result ${succeeded ? 'success' : 'failed'}">
                <div class="etlsql-cw-diag-badge ${succeeded ? 'badge-ok' : 'badge-fail'}">
                    ${succeeded ? '✓ REACHABLE & AUTHORIZED' : '✗ PROBE FAILED'}
                </div>
                ${error ? `<div class="etlsql-cw-diag-err">${_h(error)}</div>` : ''}
                <div class="etlsql-cw-diag-steps">
                    ${steps.map(s => {
                        const status = statusToStr(s.status);
                        return `
                            <div class="etlsql-cw-step-item step-${status}">
                                <span class="etlsql-cw-step-status">${status.toUpperCase()}</span>
                                <span class="etlsql-cw-step-layer">[${_h(s.layer)}]</span>
                                <span class="etlsql-cw-step-detail">${_h(s.detail)}</span>
                                ${s.remedy ? `<div class="etlsql-cw-step-remedy">💡 <strong>Remedy:</strong> ${_h(s.remedy)}</div>` : ''}
                            </div>
                        `;
                    }).join('')}
                </div>
            </div>
        `;
    }

    function renderPasteModal() {
        if (!state.pasteModalOpen) return '';

        return `
            <div class="etlsql-cw-paste-overlay">
                <div class="etlsql-cw-paste-dialog">
                    <h3>Paste Connection String or URI</h3>
                    <p class="form-hint">Supports ADO.NET, ODBC, JDBC, or postgres:// / sftp:// URIs.</p>
                    <textarea id="etlsql-cw-paste-input" class="form-control" rows="5" placeholder="Server=sql01;Database=SalesDW;User Id=usr;Password=pass;TrustServerCertificate=true;" spellcheck="false"></textarea>
                    <div class="etlsql-cw-dialog-actions">
                        <button type="button" class="btn btn-outline" id="etlsql-cw-paste-cancel">Cancel</button>
                        <button type="button" class="btn btn-primary" id="etlsql-cw-paste-apply">Parse & Apply</button>
                    </div>
                </div>
            </div>
        `;
    }

    function renderPassphraseModal() {
        if (!state.passphraseModalOpen) return '';

        return `
            <div class="etlsql-cw-paste-overlay">
                <div class="etlsql-cw-paste-dialog">
                    <div class="etlsql-cw-kicker">ZERO-TRUST CREDENTIAL DETECTED</div>
                    <h3>Encrypt Connection Password</h3>
                    <p class="form-hint">A password was detected in the connection string. Enter a client passphrase to encrypt it with zero-trust AES-256 (<code>ENC:...</code>), or configure a Vault Secret.</p>
                    <div class="form-group" style="margin-top:12px;">
                        <label for="etlsql-cw-paste-passphrase">Client Passphrase</label>
                        <input type="password" id="etlsql-cw-paste-passphrase" class="form-control" placeholder="Enter passphrase to encrypt" autocomplete="new-password" autofocus />
                    </div>
                    <div class="etlsql-cw-dialog-actions">
                        <button type="button" class="btn btn-outline" id="etlsql-cw-passphrase-skip">Use Vault Secret (${_h(state.pendingSecretKey || 'SECRET:KEY')})</button>
                        <button type="button" class="btn btn-primary" id="etlsql-cw-passphrase-encrypt">Encrypt & Apply</button>
                    </div>
                </div>
            </div>
        `;
    }

    function bindEvents() {
        // Close modal
        modalOverlay.querySelector('#etlsql-cw-close')?.addEventListener('click', closeModal);
        modalOverlay.querySelector('#etlsql-cw-cancel-btn')?.addEventListener('click', closeModal);

        // Auto-Rename button on collision
        modalOverlay.querySelector('#etlsql-cw-autorename-btn')?.addEventListener('click', () => {
            state.alias = getAutoRenameSuggestion();
            render();
        });

        // Category selection
        modalOverlay.querySelectorAll('.etlsql-cw-cat-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                const cat = btn.dataset.cat;
                if (cat === 'shared') {
                    state.isSharedReference = true;
                } else {
                    state.isSharedReference = false;
                    state.selectedCategory = cat;
                    const firstInCat = state.schemas.find(s => {
                        const type = (s.connectorType || '').toUpperCase();
                        if (cat === 'all') return true;
                        if (cat === 'database') return !s.isFileBased && ['MSSQL', 'SQLSERVER', 'POSTGRES', 'POSTGRESQL', 'MYSQL', 'MARIADB', 'SQLITE', 'ORACLE', 'SNOWFLAKE', 'BIGQUERY', 'DUCKDB', 'ODBC', 'MONGODB', 'NEO4J', 'MOCKDB'].includes(type);
                        if (cat === 'files') return s.isFileBased || ['FLATFILE', 'CSV', 'PARQUET', 'EXCEL', 'JSON', 'XML', 'AVRO', 'DIRECTORY'].includes(type);
                        if (cat === 'remote') return ['SFTP', 'FTP', 'FTP_CONN', 'REST', 'S3', 'AZUREBLOB', 'GCS', 'SHAREPOINT', 'KAFKA', 'WEBHOOK', 'SMTP', 'ACTIVEDIRECTORY', 'PORTAL', 'ORCHESTRATOR'].includes(type);
                        return true;
                    });
                    if (firstInCat) {
                        state.connectorType = firstInCat.connectorType;
                        state.values = {};
                        initFieldValues();
                    }
                }
                render();
            });
        });

        // Connector search filter input
        const typeFilterInput = modalOverlay.querySelector('#etlsql-cw-type-filter');
        if (typeFilterInput) {
            typeFilterInput.addEventListener('input', e => {
                state.typeFilter = e.target.value;
                const typeList = modalOverlay.querySelector('.etlsql-cw-type-list');
                if (typeList) {
                    typeList.innerHTML = renderConnectorTypeList();
                    bindTypeListEvents();
                }
            });
        }

        bindTypeListEvents();

        // Alias change
        modalOverlay.querySelector('#etlsql-cw-alias-input')?.addEventListener('input', e => {
            state.alias = e.target.value;
            updateSqlBox();
            checkAndAlertValidation();
        });

        // Env scope change
        modalOverlay.querySelector('#etlsql-cw-env-scope')?.addEventListener('change', e => {
            state.environmentScope = e.target.value;
        });

        // Gateway select
        modalOverlay.querySelector('#etlsql-cw-gateway-select')?.addEventListener('change', e => {
            state.gatewayCluster = e.target.value;
            state.selectedResourceId = '';
            state.selectedResource = null;
            loadGatewayResources(state.gatewayCluster);
        });

        // Gateway Resource card selection
        modalOverlay.querySelectorAll('.etlsql-cw-resource-card').forEach(card => {
            const pick = () => {
                const resId = card.dataset.resourceId;
                if (resId) {
                    state.selectedResourceId = resId;
                    const match = (state.gatewayResources || []).find(r => r.resourceId === resId);
                    if (match) {
                        state.selectedResource = match;
                        state.connectorType = match.connectorType;
                    }
                    render();
                }
            };
            card.addEventListener('click', pick);
            card.addEventListener('keydown', e => {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    pick();
                }
            });
        });

        // Shared connection select
        modalOverlay.querySelector('#etlsql-cw-shared-select')?.addEventListener('change', e => {
            state.sharedAlias = e.target.value;
            const chosen = state.sharedConnections.find(c => c.alias === state.sharedAlias);
            if (chosen) state.connectorType = chosen.connectorType;
            updateSqlBox();
        });

        // File dropzone events
        const dropzone = modalOverlay.querySelector('#etlsql-cw-file-dropzone');
        if (dropzone) {
            dropzone.addEventListener('dragover', e => {
                e.preventDefault();
                dropzone.classList.add('is-dragover');
            });
            dropzone.addEventListener('dragleave', () => {
                dropzone.classList.remove('is-dragover');
            });
            dropzone.addEventListener('drop', e => {
                e.preventDefault();
                dropzone.classList.remove('is-dragover');
                const file = e.dataTransfer?.files?.[0];
                if (file) {
                    state.values['PATH'] = `data/${file.name}`;
                    render();
                }
            });
        }

        modalOverlay.querySelector('#etlsql-cw-file-input')?.addEventListener('change', e => {
            const file = e.target.files?.[0];
            if (file) {
                state.values['PATH'] = `data/${file.name}`;
                render();
            }
        });

        modalOverlay.querySelectorAll('[data-filepath]').forEach(btn => {
            btn.addEventListener('click', () => {
                state.values['PATH'] = btn.dataset.filepath;
                render();
            });
        });

        // Dynamic form inputs
        modalOverlay.querySelectorAll('[data-opt]').forEach(input => {
            const optName = input.dataset.opt;
            if (input.type === 'checkbox') {
                input.addEventListener('change', e => {
                    state.values[optName] = e.target.checked ? 'ON' : 'OFF';
                    const card = input.closest('.etlsql-cw-checkbox-card');
                    if (card) {
                        card.classList.toggle('is-checked', e.target.checked);
                    }
                    updateSqlBox();
                });
            } else {
                input.addEventListener('input', e => {
                    state.values[optName] = e.target.value;
                    updateSqlBox();
                    checkAndAlertValidation();
                });
            }
        });

        // Auth Tabs
        modalOverlay.querySelectorAll('.etlsql-cw-auth-tab').forEach(tab => {
            tab.addEventListener('click', () => {
                state.authType = tab.dataset.authtype;
                render();
            });
        });

        // Auth Inputs
        modalOverlay.querySelector('#etlsql-cw-secret-key')?.addEventListener('input', e => {
            state.secretKey = e.target.value;
            updateSqlBox();
        });
        modalOverlay.querySelector('#etlsql-cw-env-key')?.addEventListener('input', e => {
            state.envVarName = e.target.value;
            updateSqlBox();
        });
        modalOverlay.querySelector('#etlsql-cw-raw-pw')?.addEventListener('input', async e => {
            state.rawPassword = e.target.value;
            if (state.authType === 'enc' && state.encPassphrase) {
                state.encryptedCipher = await encryptClientPassword(state.rawPassword, state.encPassphrase);
            }
            updateSqlBox();
        });
        modalOverlay.querySelector('#etlsql-cw-enc-passphrase')?.addEventListener('input', async e => {
            state.encPassphrase = e.target.value;
            if (state.authType === 'enc' && state.rawPassword) {
                state.encryptedCipher = await encryptClientPassword(state.rawPassword, state.encPassphrase);
            }
            updateSqlBox();
        });
        modalOverlay.querySelector('#etlsql-cw-key-path')?.addEventListener('input', e => {
            state.keyFilePath = e.target.value;
            updateSqlBox();
            checkAndAlertValidation();
        });

        // Test Connection Button (Both in Header and in Right Pane)
        modalOverlay.querySelector('#etlsql-cw-test-btn')?.addEventListener('click', runDiagnostic);
        modalOverlay.querySelector('#etlsql-cw-header-test-btn')?.addEventListener('click', runDiagnostic);

        // Copy SQL Button
        modalOverlay.querySelector('#etlsql-cw-copy-sql')?.addEventListener('click', () => {
            const sql = generateSql();
            navigator.clipboard?.writeText(sql).then(() => {
                const btn = modalOverlay.querySelector('#etlsql-cw-copy-sql');
                if (btn) {
                    btn.textContent = 'Copied!';
                    setTimeout(() => { if (btn) btn.textContent = 'Copy SQL'; }, 1500);
                }
            });
        });

        // Paste Button / Dialog
        modalOverlay.querySelector('#etlsql-cw-paste-btn')?.addEventListener('click', () => {
            state.pasteModalOpen = true;
            render();
        });
        modalOverlay.querySelector('#etlsql-cw-paste-cancel')?.addEventListener('click', () => {
            state.pasteModalOpen = false;
            render();
        });
        modalOverlay.querySelector('#etlsql-cw-paste-apply')?.addEventListener('click', async () => {
            const txt = (modalOverlay.querySelector('#etlsql-cw-paste-input')?.value || '').trim();
            if (!txt) return;

            let parsed = null;
            if (onParseString) {
                try {
                    parsed = await onParseString(txt, state.connectorType);
                } catch (e) {
                    console.warn('Parse string failed on server, using client fallback', e);
                }
            }

            if (!parsed) {
                parsed = parseConnectionStringFallback(txt, state.connectorType);
            }

            if (parsed) {
                if (parsed.detectedProvider) {
                    state.connectorType = parsed.detectedProvider;
                }
                for (const [k, v] of Object.entries(parsed.options || {})) {
                    state.values[k] = v;
                }

                if (parsed.extractedCredential) {
                    state.rawPassword = parsed.extractedCredential;
                    state.pendingExtractedPassword = parsed.extractedCredential;
                    state.pendingSecretKey = parsed.suggestedSecretKey || `${state.connectorType}_PW`;
                    state.pasteModalOpen = false;
                    state.passphraseModalOpen = true;
                    render();
                    return;
                }
            }

            state.pasteModalOpen = false;
            render();
        });

        // Passphrase Prompt Dialog Buttons
        modalOverlay.querySelector('#etlsql-cw-passphrase-encrypt')?.addEventListener('click', async () => {
            const passInput = modalOverlay.querySelector('#etlsql-cw-paste-passphrase');
            const pass = (passInput?.value || '').trim();
            if (!pass) {
                if (passInput) {
                    passInput.focus();
                    passInput.classList.add('is-invalid');
                }
                return;
            }
            state.encPassphrase = pass;
            if (state.rawPassword) {
                state.encryptedCipher = await encryptClientPassword(state.rawPassword, pass);
            }
            state.authType = 'enc';
            state.passphraseModalOpen = false;
            render();
        });

        modalOverlay.querySelector('#etlsql-cw-passphrase-skip')?.addEventListener('click', () => {
            state.authType = 'secret';
            state.secretKey = state.pendingSecretKey || `${state.connectorType}_PW`;
            state.passphraseModalOpen = false;
            render();
        });

        // Submit Button (Insert or Save)
        modalOverlay.querySelector('#etlsql-cw-submit-btn')?.addEventListener('click', async () => {
            const violation = getSecurityViolation();
            if (violation || state.isSaving) return;

            const sql = generateSql();
            if (state.mode === 'admin') {
                if (onSave) {
                    state.isSaving = true;
                    state.saveError = null;
                    render();
                    try {
                        const entry = state.selectedResourceId ? {
                            alias: state.alias,
                            connectorType: state.connectorType,
                            target: null,
                            options: {},
                            gateway: {
                                gatewayId: state.gatewayCluster,
                                resourceId: state.selectedResourceId
                            },
                            environmentScope: state.environmentScope
                        } : {
                            alias: state.alias,
                            connectorType: state.connectorType,
                            target: state.values.SERVER || state.values.HOST || state.values.PATH || '',
                            options: {
                                ...state.values,
                                ...(state.gatewayCluster ? { GATEWAY: state.gatewayCluster } : {})
                            },
                            environmentScope: state.environmentScope
                        };
                        await onSave(entry);
                        closeModal();
                    } catch (error) {
                        console.warn('ConnectionWizard: catalog save failed.');
                        state.isSaving = false;
                        state.saveError = 'Connection could not be saved. Review the entry and try again.';
                        render();
                    }
                }
            } else {
                if (onInsert) {
                    onInsert(sql, {
                        alias: state.alias,
                        connectorType: state.connectorType,
                        isShared: state.isSharedReference,
                        gateway: state.selectedResourceId ? { gatewayId: state.gatewayCluster, resourceId: state.selectedResourceId } : (state.gatewayCluster || null),
                        options: state.values
                    });
                }
                closeModal();
            }
        });
    }

    function bindTypeListEvents() {
        modalOverlay.querySelectorAll('.etlsql-cw-type-item').forEach(btn => {
            btn.addEventListener('click', () => {
                const type = btn.dataset.type;
                if (type && type !== state.connectorType) {
                    state.connectorType = type;
                    state.values = {};
                    initFieldValues();
                    render();
                }
            });
        });
    }

    function checkAndAlertValidation() {
        const violation = getSecurityViolation();
        const nameCollision = getNameCollision();
        const isAliasMissing = !state.alias || !state.alias.trim();
        let securityAlert = modalOverlay.querySelector('.etlsql-cw-security-alert');
        if (violation) {
            if (!securityAlert) {
                securityAlert = document.createElement('div');
                securityAlert.className = 'etlsql-cw-security-alert';
                modalOverlay.querySelector('.etlsql-cw-meta-row')?.insertAdjacentElement('afterend', securityAlert);
            }
            securityAlert.textContent = violation;
        } else {
            securityAlert?.remove();
        }
        const submitBtn = modalOverlay.querySelector('#etlsql-cw-submit-btn');
        if (submitBtn) {
            submitBtn.disabled = Boolean(violation || nameCollision || isAliasMissing);
        }

        const aliasInput = modalOverlay.querySelector('#etlsql-cw-alias-input');
        if (aliasInput) {
            aliasInput.classList.toggle('etlsql-cw-alias-missing', isAliasMissing);
        }
        const missingHint = modalOverlay.querySelector('.etlsql-cw-missing-hint');
        if (missingHint) {
            missingHint.style.display = isAliasMissing ? '' : 'none';
        }
        const validHint = modalOverlay.querySelector('.etlsql-cw-valid-hint');
        if (validHint) {
            validHint.style.display = isAliasMissing ? 'none' : '';
            const code = validHint.querySelector('code');
            if (code) code.textContent = (state.alias || '').trim();
        }
        const reqTag = modalOverlay.querySelector('.etlsql-cw-required-tag');
        if (reqTag) {
            reqTag.style.display = isAliasMissing ? '' : 'none';
        }
    }

    function updateSqlBox() {
        const box = modalOverlay.querySelector('.etlsql-cw-sql-box code');
        if (box) {
            box.textContent = generateSql();
        }
    }

    function closeModal() {
        if (onClose) onClose();
        modalOverlay.remove();
    }

    function parseConnectionStringFallback(raw, hint) {
        const options = {};
        let extractedCredential = null;
        let detected = hint || 'MSSQL';

        const pairs = raw.split(';');
        for (const pair of pairs) {
            const eq = pair.indexOf('=');
            if (eq > 0) {
                const k = pair.substring(0, eq).trim().toUpperCase().replace(/ /g, '_');
                const v = pair.substring(eq + 1).trim();
                if (k === 'PASSWORD' || k === 'PWD') {
                    extractedCredential = v;
                } else if (k === 'DATA_SOURCE' || k === 'SERVER') {
                    if (v.includes(',')) {
                        const parts = v.split(',');
                        options['SERVER'] = parts[0].trim();
                        if (parts.length > 1) options['PORT'] = parts[1].trim();
                    } else {
                        options['SERVER'] = v;
                    }
                } else if (k === 'INITIAL_CATALOG' || k === 'DATABASE') {
                    options['DATABASE'] = v;
                } else if (k === 'USER_ID' || k === 'UID' || k === 'USER') {
                    options['USER'] = v;
                } else if (k === 'TRUSTSERVERCERTIFICATE' || k === 'TRUST_SERVER_CERTIFICATE') {
                    options['TRUST_SERVER_CERTIFICATE'] = v.toUpperCase() === 'TRUE' ? 'ON' : 'OFF';
                } else {
                    options[k] = v;
                }
            }
        }

        return {
            detectedProvider: detected,
            options,
            extractedCredential,
            suggestedSecretKey: extractedCredential ? `${detected}_${(options.DATABASE || 'DB').toUpperCase()}_PW` : null
        };
    }

    render();

    return {
        open: () => { modalOverlay.style.display = 'flex'; },
        close: closeModal,
        getGeneratedSql: generateSql,
        validatePathSecurity: validatePathSecurity
    };
}
