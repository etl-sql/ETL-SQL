// Story: the structure / lineage DAG (renderDag).
import { buildKitchenSinkGraph, buildSmallGraph, buildEdwExampleGraph, buildCrossScriptGraph } from '../fixture.js';
import { importFresh, DESIGNER_JS } from '../util.js';

const BUILDERS = {
  kitchen: buildKitchenSinkGraph,
  small:   buildSmallGraph,
  edw:     buildEdwExampleGraph,
  xscript: buildCrossScriptGraph,
};

export default {
  id: 'dag',
  title: 'DAG / lineage',
  subtitle: 'renderDag()',
  fixtures: [
    { id: 'kitchen', label: 'Kitchen Sink (~106 nodes)' },
    { id: 'small',   label: 'Small report (5 nodes)' },
    { id: 'edw',     label: 'EDW example (salesBar drill-down)' },
    { id: 'xscript', label: 'Cross-script (dataset built separately)' },
  ],
  async mount(stage, fixtureId, ctx) {
    const graph = (BUILDERS[fixtureId] ?? buildKitchenSinkGraph)();
    ctx.stat(`${graph.nodes.length} nodes · ${graph.edges.length} edges`);
    const mod = await importFresh(DESIGNER_JS);
    return mod.renderDag(stage, graph, { theme: 'portal' });
  },
};
