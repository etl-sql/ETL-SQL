import assert from 'node:assert/strict';
import { readFolderTree, readReports, readConsumerHome, readSearchResults, filterReports, sortReports, statusBadge, activityLine,
  renderFolderTree, renderFolderGrid, renderCatalogList, renderConsumerCard, renderFailure, renderSearchResults }
  from '../src/ETL-SQL.Portal/wwwroot/js/report-library-view.js';

// The server sends roots that carry their own children; a subfolder must survive the read and the render.
const tree = readFolderTree([{ id: 1, name: 'Finance', path: '/Finance', children: [{ id: 2, name: 'Quarterly', path: '/Finance/Quarterly',
  children: [{ id: 4, name: 'Q3', children: [] }] }] }, { id: 3, name: 'Operations' }]);
assert.deepEqual(tree.map(folder => folder.name), ['Finance', 'Operations']);
assert.equal(tree[0].children[0].children[0].name, 'Q3');
assert.deepEqual(tree[1].children, []);
const markup = renderFolderTree(tree);
assert.equal((markup.match(/class="folder-item"/g) ?? []).length, 4);
assert.match(markup, /data-id="1"[^>]*>[\s\S]*<div class="folder-children" role="group">[\s\S]*data-id="2"/);
assert.match(markup, /aria-expanded="true"\s+data-id="1"/);
assert.doesNotMatch(markup, /aria-expanded="true"\s+data-id="3"/);
assert.match(markup, /data-id="4"[^>]*style="margin-left:20px"/);
assert.doesNotMatch(renderFolderTree(readFolderTree([{ id: 9, name: '<b>x</b>' }])), /<b>x/);
for (const invalid of [null, {}, [null], [{ id: 0, name: 'x' }], [{ id: 1, name: '' }], [{ id: 1, name: 'x', children: 'none' }],
  [{ id: 1, name: 'x', children: [{ id: 2 }] }]])
  assert.throws(() => readFolderTree(invalid), /folder list is unavailable or incomplete/, JSON.stringify(invalid));

const raw = { id: 7, folderId: 1, name: 'Regional sales', description: 'By region', path: '/Finance/Regional sales', isFavorite: true,
  isStale: true, snapshotBuiltAt: '2026-10-01T05:00:00Z', lastRefreshStatus: null };
const [report] = readReports([raw]);
assert.equal(report.isFavorite, true);
assert.equal(report.lastRefreshStatus, '');
assert.equal(readReports([{ id: 8, name: 'x', folderId: 'one', snapshotBuiltAt: 'soon', isFavorite: 'yes' }])[0].folderId, null);
assert.equal(readReports([{ id: 8, name: 'x', snapshotBuiltAt: 'soon' }])[0].snapshotBuiltAt, null);
for (const invalid of [null, {}, [null], [{ id: '7', name: 'x' }], [{ id: 7 }]])
  assert.throws(() => readReports(invalid), /report list is unavailable or incomplete/);
assert.deepEqual(Object.keys(readConsumerHome({ favorites: [raw] })), ['favorites', 'recent', 'featured', 'popular']);
assert.throws(() => readConsumerHome(null), /report home is unavailable or incomplete/);
assert.throws(() => readConsumerHome({ recent: [{}] }), /report home is unavailable or incomplete/);

const found = readSearchResults([{ type: 'Folder', id: 2, name: 'Quarterly', path: '/Finance/Quarterly' }, { type: 'REPORT', ...raw }, { type: 'Dataset', id: 5 }]);
assert.equal(found.total, 3);
assert.equal(found.folders.length, 1);
assert.equal(found.reports.length, 1);
assert.throws(() => readSearchResults([{ type: 'report', id: 'x' }]), /search results are unavailable or incomplete/);
assert.match(renderSearchResults('"><i>q', found), /&quot;&gt;&lt;i&gt;q/);
assert.match(renderSearchResults('none', readSearchResults([])), /No catalog matches/);

const reports = readReports([raw, { id: 8, name: 'Accounts aging', description: 'Open receivables', snapshotBuiltAt: '2026-10-09T05:00:00Z' },
  { id: 9, name: 'Margin', description: '', snapshotBuiltAt: null }]);
assert.deepEqual(filterReports(reports, ' RECEIV ').map(item => item.id), [8]);
assert.equal(filterReports(reports, '  ').length, 3);
assert.deepEqual(sortReports(reports, 'name').map(item => item.id), [8, 9, 7]);
assert.deepEqual(sortReports(reports, 'recent').map(item => item.id), [8, 7, 9]);
assert.deepEqual(sortReports(reports, 'stale').map(item => item.id), [7, 8, 9]);
assert.deepEqual(reports.map(item => item.id), [7, 8, 9], 'sorting must not reorder the loaded list');

const now = Date.parse('2026-10-10T12:00:00Z');
const state = extra => readReports([{ id: 1, name: 'r', ...extra }])[0];
assert.equal(activityLine(state({}), now), 'Ready for first run');
assert.equal(activityLine(state({ snapshotBuiltAt: '2026-10-10T07:00:00Z' }), now), 'Updated 5 hr ago');
assert.equal(activityLine(state({ snapshotBuiltAt: '2026-10-10T07:00:00Z', lastViewedAt: '2026-10-10T11:00:00Z' }), now), 'Viewed 1 hr ago');
assert.equal(activityLine(state({ lastRefreshStatus: 'Running' }), now), 'Running now');
assert.equal(activityLine(state({ lastRefreshStatus: 'Failed' }), now), 'Last run failed');
assert.equal(activityLine(state({ lastRefreshStatus: 'Cancelled', lastRefreshCompletedAt: '2026-10-10T11:30:00Z' }), now), 'Run cancelled 30 min ago');
assert.match(statusBadge(state({ lastRefreshStatus: 'Failed', lastRefreshError: '"><b>boom' })), /title="&quot;&gt;&lt;b&gt;boom"/);
assert.equal(statusBadge(state({})), '');
assert.match(statusBadge(state({ isStale: true })), /Stale/);

// Every server-supplied string is text in every card.
const hostile = readReports([{ id: 5, folderId: 2, name: '<img src=x onerror=1>', description: '"><script>x</script>', path: '/a"b/<c>', certification: '<u>gold</u>' }]);
for (const card of [renderConsumerCard(hostile[0]), renderFolderGrid(hostile, hostile), renderCatalogList('Favorites', hostile, '')])
  assert.doesNotMatch(card, /<img|<script|<u>gold|data-folder-path="\/a"b/);
assert.match(renderFolderGrid(hostile, hostile), /data-report-id="5"[^>]*data-folder-id="2"/);
assert.match(renderFolderGrid(reports, []), /No matching reports/);
assert.match(renderFolderGrid([], []), /No reports here yet/);
assert.match(renderCatalogList('Favorites', [], 'none yet'), /No favorites reports/);
assert.match(renderFailure('Reports did not load', '<b>why</b>'), /role="alert"[\s\S]*&lt;b&gt;why&lt;\/b&gt;[\s\S]*data-library-action="refresh"/);

console.log('report library checks passed');
