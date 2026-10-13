import assert from 'node:assert/strict';
import { readScheduleLinks, readNotificationLinks, readJobStates, readHistory, readGrants, readDependencyChain,
  readAudit, readQualityFailures, readQualityStatuses } from '../../src/ETL-SQL.Portal/wwwroot/js/orchestrator-detail-responses.js';

const rejects = (read, values, pattern) => { for (const value of values) assert.throws(() => read(value), pattern, JSON.stringify(value)); };

// Attached schedules and notifications: an entry without its name cannot be detached, so it is not drawn.
assert.deepEqual(readScheduleLinks([]), []);
assert.deepEqual(readScheduleLinks([{ jobId: 'j', scheduleId: 's', scheduleName: 'nightly', nextRun: '2026-10-10T02:00:00Z', lastRun: null }]),
  [{ scheduleName: 'nightly', nextRun: '2026-10-10T02:00:00Z' }]);
assert.equal(readScheduleLinks([{ scheduleName: 'nightly' }])[0].nextRun, null);
rejects(readScheduleLinks, [null, {}, [null], [{}], [{ scheduleName: '' }], [{ scheduleName: 'n', nextRun: 'soon' }]], /Invalid attached schedule response/);
assert.deepEqual(readNotificationLinks([{ notificationName: 'ops', trigger: 'Failure', jobName: 'j' }]), [{ notificationName: 'ops', trigger: 'Failure' }]);
rejects(readNotificationLinks, [undefined, [{ notificationName: 'ops' }], [{ trigger: 'Failure' }]], /Invalid attached notification response/);

assert.deepEqual(readJobStates([{ jobName: 'j', stateKey: 'k', stateValue: null, updatedAt: '2026-10-09T11:00:00Z' }]),
  [{ stateKey: 'k', stateValue: null, updatedAt: '2026-10-09T11:00:00Z' }]);
rejects(readJobStates, [[{ stateKey: 'k' }], [{ stateKey: '', updatedAt: '2026-10-09T11:00:00Z' }],
  [{ stateKey: 'k', stateValue: 5, updatedAt: '2026-10-09T11:00:00Z' }]], /Invalid watermark response/);

const run = { id: 42, jobName: 'j', startTime: '2026-10-09T02:00:00Z', endTime: null, status: 'Failed', rowsProcessed: 12,
  checkpointLabel: 'load', hasResumeSession: true };
assert.deepEqual(readHistory([run]), [{ id: 42, startTime: run.startTime, endTime: null, status: 'Failed', rowsProcessed: 12,
  checkpointLabel: 'load', hasResumeSession: true }]);
// A run that does not say it can resume cannot: the button is offered only on an explicit true.
assert.equal(readHistory([{ ...run, hasResumeSession: undefined }])[0].hasResumeSession, false);
rejects(readHistory, [[{ ...run, id: '42' }], [{ ...run, startTime: 'yesterday' }], [{ ...run, status: '' }],
  [{ ...run, rowsProcessed: -1 }], [{ ...run, hasResumeSession: 'yes' }], [{ ...run, endTime: 'later' }]], /Invalid run history response/);

assert.deepEqual(readGrants([{ objectId: 'j', principalKind: 'GROUP', principalId: 'ops', permission: 'READ', version: 1 }]),
  [{ principalKind: 'GROUP', principalId: 'ops', permission: 'READ' }]);
rejects(readGrants, [{}, [{ principalKind: 'GROUP', principalId: '', permission: 'READ' }], [{ principalId: 'ops', permission: 'READ' }]],
  /Invalid access grant response/);

const node = (name, isCurrent) => ({ id: name, name, displayName: null, isEnabled: true, lastRun: null, nextRun: null, isCurrent });
const chain = { jobName: 'daily', nodes: [node('daily', true), node('hourly', false)],
  edges: [{ from: 'hourly', to: 'daily', type: 'Data', detail: 'Writes orders' }], upstream: ['hourly'], downstream: [], sharedSchedules: [] };
assert.equal(readDependencyChain(chain, 'DAILY').edges.length, 1);
// A chain for another job, an edge to an unlisted job, or no single current job is not this job's chain.
for (const invalid of [null, { ...chain, jobName: 'other' }, { ...chain, upstream: 'hourly' }, { ...chain, nodes: [node('daily', false)], edges: [] },
  { ...chain, nodes: [node('daily', true), node('hourly', true)] }, { ...chain, edges: [{ from: 'ghost', to: 'daily', type: 'Data', detail: '' }] },
  { ...chain, nodes: [{ ...node('daily', true), isEnabled: 'true' }], edges: [] }])
  assert.throws(() => readDependencyChain(invalid, 'daily'), /Invalid dependency response/, JSON.stringify(invalid));

const audit = { id: 1, action: 'JobScriptEdited', timestamp: '2026-10-09T10:00:00Z', actorId: 'operator', userId: null, detail: null };
assert.equal(readAudit([audit])[0].action, 'JobScriptEdited');
rejects(readAudit, [[{ ...audit, action: '' }], [{ ...audit, timestamp: null }], [{ ...audit, userId: 'one' }]], /Invalid audit trail response/);

const failure = { runId: 42, jobName: 'daily', startTime: '2026-10-09T02:00:00Z', targetTable: null, columnName: 'amount',
  rule: 'NOT NULL', action: 'QUARANTINE', failureCount: 3 };
assert.equal(readQualityFailures([failure])[0].failureCount, 3);
rejects(readQualityFailures, [[{ ...failure, failureCount: '3' }], [{ ...failure, rule: '' }], [{ ...failure, jobName: null }]],
  /Invalid data-quality failure response/);
const status = { runId: '42', jobName: null, startTime: '2026-10-09T02:00:00Z', status: 'Failed', rowsWarned: 7, rowsQuarantined: 3,
  failedRuleCount: 1, freshnessState: 'Fresh' };
assert.equal(readQualityStatuses([status])[0].rowsQuarantined, 3);
rejects(readQualityStatuses, [[{ ...status, rowsQuarantined: null }], [{ ...status, rowsWarned: 1.5 }], [{ ...status, freshnessState: '' }]],
  /Invalid data-quality status response/);

console.log('orchestrator detail response checks passed');
