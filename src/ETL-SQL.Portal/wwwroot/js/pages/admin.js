/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/pages/admin.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { createAdminUsers } from '../admin-users.js';
import { createAdminGroups } from '../admin-groups.js';
import { createAdminFolders } from '../admin-folders.js';
import { createAdminReports } from '../admin-reports.js';
import { createAdminAudit } from '../admin-audit.js';
import { createAdminSubscriptions } from '../admin-subscriptions.js';
import { createAdminSettings } from '../admin-settings.js';
import { auth, authApi, adminApi, datasetsApi, catalogApi, secretsApi, connectionsApi, gatewaysApi, policyAuthorityApi } from '../api.js';
import { applyPortalBranding, initTheme } from '../branding.js';
import { renderDag } from '../../designer/designer.js';
import { createDatasetsAdmin } from '../datasets-admin.js';
import { createSecretsAdmin } from '../secrets-admin.js';
import { createConnectionsAdmin } from '../connections-admin.js';
import { createGatewaysAdmin } from '../gateways-admin.js';
import { createPolicyAuthorityAdmin } from '../policy-authority-admin.js';
import { createOperationsAdmin } from '../operations-admin.js';
import { getSessionIdentity, hasRole, renderSessionIdentity } from '../session-identity.js';
import { applyNavigationSafely } from '../portal-nav.js';
import { installPortalStateStyles } from '../portal-states.js';
import { renderPortalHeader } from '../portal-header.js';
import { installDialogAccessibility } from '../dialog-a11y.js';
function requiredElement(id) {
    const element = document.getElementById(id);
    if (!element)
        throw new TypeError(`Admin page element is missing: ${id}`);
    return element;
}
function mountAdminPage() {
    renderPortalHeader();
    installDialogAccessibility();
    if (!auth.isLoggedIn()) {
        window.location.href = '/login.html';
        return;
    }
    try {
        const identity = getSessionIdentity(auth.getToken());
        renderSessionIdentity(identity, document.getElementById('topbarUser'));
        if (!hasRole(identity, 'Admin')) {
            window.location.href = '/index.html';
            return;
        }
    }
    catch {
        window.location.href = '/login.html';
        return;
    }
    void applyPortalBranding();
    initTheme();
    installPortalStateStyles();
    applyNavigationSafely();
    const listeners = [];
    let disposed = false;
    function listen(element, name, callback) {
        element.addEventListener(name, callback);
        listeners.push(() => element.removeEventListener(name, callback));
    }
    listen(requiredElement('logoutBtn'), 'click', () => { void authApi.logout(); });
    const users = createAdminUsers(), groups = createAdminGroups(), folders = createAdminFolders();
    const reports = createAdminReports(), audit = createAdminAudit(), subscriptions = createAdminSubscriptions();
    const settings = createAdminSettings();
    let datasets = null;
    let secrets = null;
    let connections = null;
    let gateways = null;
    let policy = null;
    let operations = null;
    const tabLoaders = {
        users: () => users.reload(), groups: () => groups.reload(), folders: () => folders.reload(),
        reports: () => reports.reload(), audit: () => audit.reload(), subscriptions: () => subscriptions.reload(),
        settings: () => settings.reload(),
        datasets() {
            datasets ??= createDatasetsAdmin({ host: requiredElement('panel-datasets'), datasetsApi, adminApi, catalogApi, renderDag });
            return datasets.load();
        },
        secrets() {
            secrets ??= createSecretsAdmin({ host: requiredElement('panel-secrets'), secretsApi });
            return secrets.load();
        },
        connections() {
            connections ??= createConnectionsAdmin({ host: requiredElement('panel-connections'), connectionsApi });
            return connections.load();
        },
        gateways() {
            gateways ??= createGatewaysAdmin({ host: requiredElement('panel-gateways'), gatewaysApi });
            return gateways.load();
        },
        policy() {
            policy ??= createPolicyAuthorityAdmin({ host: requiredElement('panel-policy'), policyAuthorityApi });
            return policy.load();
        },
        operations() {
            operations ??= createOperationsAdmin({ host: requiredElement('panel-operations'), adminApi });
            return operations.load();
        }
    };
    const loaded = new Set(['users']);
    const tabs = document.querySelectorAll('.admin-tab');
    const panels = document.querySelectorAll('.admin-panel');
    tabs.forEach(button => listen(button, 'click', () => {
        const value = button.dataset.tab;
        if (disposed || !value || !Object.hasOwn(tabLoaders, value))
            return;
        const tab = value;
        tabs.forEach(other => { other.classList.remove('active'); other.setAttribute('aria-selected', 'false'); });
        panels.forEach(panel => panel.classList.remove('active'));
        button.classList.add('active');
        button.setAttribute('aria-selected', 'true');
        requiredElement(`panel-${tab}`).classList.add('active');
        if (!loaded.has(tab)) {
            loaded.add(tab);
            void tabLoaders[tab]();
        }
    }));
    void users.reload();
    document.querySelectorAll('.modal-overlay').forEach(modal => listen(modal, 'click', event => {
        if (event.target === modal)
            modal.style.display = 'none';
    }));
    const keydown = (event) => {
        if (event.key !== 'Escape')
            return;
        const open = [...document.querySelectorAll('.modal-overlay')].reverse()
            .find(modal => modal.style.display !== 'none');
        if (open)
            open.style.display = 'none';
    };
    document.addEventListener('keydown', keydown);
    window.addEventListener('pagehide', () => {
        disposed = true;
        listeners.splice(0).forEach(remove => remove());
        document.removeEventListener('keydown', keydown);
        for (const owner of [users, groups, folders, reports, audit, subscriptions, settings,
            datasets, secrets, connections, gateways, policy, operations])
            owner?.dispose();
    }, { once: true });
}
mountAdminPage();
