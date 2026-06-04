#!/usr/bin/env node

const portalBaseUrl = process.env.CAPACITY_PORTAL_URL ?? 'http://127.0.0.1:5110';
const orchestratorBaseUrl = process.env.CAPACITY_ORCHESTRATOR_URL ?? 'http://127.0.0.1:5111';
const orchestratorApiKey = required('CAPACITY_ORCHESTRATOR_API_KEY');
const initialAdminPassword = required('CAPACITY_INITIAL_ADMIN_PASSWORD');
const adminPassword = required('CAPACITY_ADMIN_PASSWORD');
const viewerPassword = required('CAPACITY_VIEWER_PASSWORD');
const publisherPassword = required('CAPACITY_PUBLISHER_PASSWORD');

const initialLogin = await request(portalBaseUrl, 'POST', '/api/auth/login', {
  username: 'admin',
  password: initialAdminPassword,
});
await request(portalBaseUrl, 'POST', '/api/auth/change-password', {
  currentPassword: initialAdminPassword,
  newPassword: adminPassword,
}, initialLogin.token);
const admin = await request(portalBaseUrl, 'POST', '/api/auth/login', {
  username: 'admin',
  password: adminPassword,
});

const viewer = await request(portalBaseUrl, 'POST', '/api/admin/users', {
  username: 'capacity_viewer',
  email: 'capacity_viewer@example.invalid',
  password: `${viewerPassword}Temp1!`,
  role: 'Viewer',
}, admin.token);
const publisher = await request(portalBaseUrl, 'POST', '/api/admin/users', {
  username: 'capacity_publisher',
  email: 'capacity_publisher@example.invalid',
  password: `${publisherPassword}Temp1!`,
  role: 'Publisher',
}, admin.token);
await completeFirstLogin('capacity_viewer', `${viewerPassword}Temp1!`, viewerPassword);
await completeFirstLogin('capacity_publisher', `${publisherPassword}Temp1!`, publisherPassword);
const group = await request(portalBaseUrl, 'POST', '/api/admin/groups', {
  name: 'Capacity Reference Users',
  description: 'Reference baseline access group',
}, admin.token);
await request(portalBaseUrl, 'POST', `/api/admin/groups/${group.id}/members/bulk-add`, {
  userIds: [viewer.id, publisher.id],
}, admin.token);

const folder = await request(portalBaseUrl, 'POST', '/api/folders', {
  name: 'Capacity Reference',
  parentId: null,
}, admin.token);
await request(portalBaseUrl, 'POST', `/api/folders/${folder.id}/acl`, {
  groupId: group.id,
  permission: 1,
}, admin.token);

const report = await request(portalBaseUrl, 'POST', '/api/reports', {
  folderId: folder.id,
  name: 'Capacity Reference Report',
  description: 'Deterministic local capacity baseline report',
  scriptPath: 'capacity-reference.rptsql',
}, admin.token);
const execution = await request(portalBaseUrl, 'POST', `/api/reports/${report.id}/execute`, {
  parameters: {},
}, admin.token);
await waitForExecution(execution.jobId, admin.token);
await request(portalBaseUrl, 'GET', `/api/reports/${report.id}/snapshot/manifest`, undefined, admin.token);
await request(portalBaseUrl, 'GET', `/api/reports/${report.id}/export/csv`, undefined, admin.token);

await request(orchestratorBaseUrl, 'POST', '/api/scheduled-jobs', {
  name: 'capacity-noop',
  scriptText: "SELECT 1 AS Answer;",
  interval: 1,
  unit: 'HOUR',
  maxRetries: 0,
  retryDelaySeconds: 1,
  hashPolicy: 'Warn',
}, null, { 'X-Orchestrator-Key': orchestratorApiKey });

console.log(JSON.stringify({
  folderId: folder.id,
  reportId: report.id,
  viewerId: viewer.id,
  publisherId: publisher.id,
  executionJobId: execution.jobId,
  jobName: 'capacity-noop',
}, null, 2));

function required(name) {
  const value = process.env[name];
  if (!value) throw new Error(`${name} is required.`);
  return value;
}

async function completeFirstLogin(username, currentPassword, newPassword) {
  const login = await request(portalBaseUrl, 'POST', '/api/auth/login', { username, password: currentPassword });
  await request(portalBaseUrl, 'POST', '/api/auth/change-password', { currentPassword, newPassword }, login.token);
}

async function waitForExecution(jobId, token) {
  const deadline = Date.now() + 60_000;
  while (Date.now() < deadline) {
    const job = await request(portalBaseUrl, 'GET', `/api/jobs/${jobId}`, undefined, token);
    if (job.status === 'Completed') return;
    if (job.status === 'Failed' || job.status === 'Cancelled') {
      throw new Error(`Report execution ${jobId} ended with status ${job.status}: ${job.error ?? 'no error reported'}`);
    }
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  throw new Error(`Report execution ${jobId} did not complete within 60 seconds.`);
}

async function request(baseUrl, method, path, body, token = null, extraHeaders = {}) {
  const headers = { ...extraHeaders };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(new URL(path, baseUrl), {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  if (!response.ok) throw new Error(`${method} ${path} failed with ${response.status}: ${text}`);
  if (!text) return {};
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}
