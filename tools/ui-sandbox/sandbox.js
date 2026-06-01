// UI sandbox shell — picks a story + fixture and mounts it into the stage.
import { stories } from './stories/index.js';

const $stories   = document.getElementById('stories');
const $fixture   = document.getElementById('fixtureSel');
const $stage     = document.getElementById('stage');
const $stat      = document.getElementById('stat');
const $title     = document.getElementById('storyTitle');
const $reload    = document.getElementById('reloadBtn');

let current  = stories[0];
let instance = null;

function renderStoryNav() {
  $stories.replaceChildren();
  for (const s of stories) {
    const b = document.createElement('button');
    b.className = 'story-link' + (s === current ? ' is-active' : '');
    const t = document.createElement('span'); t.className = 'story-link-title'; t.textContent = s.title;
    const c = document.createElement('span'); c.className = 'story-link-sub';   c.textContent = s.subtitle || '';
    b.append(t, c);
    b.addEventListener('click', () => { current = s; selectStory(); });
    $stories.appendChild(b);
  }
}

function renderFixtures() {
  $fixture.replaceChildren();
  for (const f of (current.fixtures ?? [])) {
    const o = document.createElement('option');
    o.value = f.id; o.textContent = f.label;
    $fixture.appendChild(o);
  }
  $fixture.style.display = (current.fixtures?.length ?? 0) > 1 ? '' : 'none';
}

async function mount() {
  if (instance) { try { instance.dispose?.(); } catch { /* ignore */ } instance = null; }
  $stage.replaceChildren();
  $stat.textContent = '';
  const ctx = { stat: (t) => { $stat.textContent = t; } };
  try {
    instance = await current.mount($stage, $fixture.value, ctx);
  } catch (err) {
    const pre = document.createElement('pre');
    pre.className = 'sandbox-err';
    pre.textContent = `Mount failed for "${current.title}":\n${err.stack || err.message}`;
    $stage.replaceChildren(pre);
    console.error(err);
  }
}

function selectStory() {
  $title.textContent = current.title;
  renderStoryNav();
  renderFixtures();
  mount();
}

$fixture.addEventListener('change', mount);
$reload.addEventListener('click', mount);
window.addEventListener('resize', () => instance?.resize?.());

selectStory();
