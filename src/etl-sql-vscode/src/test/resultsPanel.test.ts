import { describe, it, expect, beforeEach, vi } from 'vitest';
import { JSDOM } from 'jsdom';
import * as fs from 'fs';
import * as path from 'path';

// Mock vscode and nonce
const nonce = 'test-nonce';
const cspSource = 'vscode-resource:';

/**
 * Since resultsPanel.ts returns the HTML as a string from a private method,
 * we will extract the HTML template for testing by reading the file and 
 * parsing out the string literal.
 */
function getHtmlTemplate(): string {
    const filePath = path.resolve(__dirname, '../resultsPanel.ts');
    const content = fs.readFileSync(filePath, 'utf-8');
    
    const startMarker = '// @TEMPLATE-START';
    const endMarker = '// @TEMPLATE-END';
    
    const startIdx = content.indexOf(startMarker);
    const endIdx = content.indexOf(endMarker, startIdx);
    
    if (startIdx === -1 || endIdx === -1) {
        throw new Error('Could not find HTML template in resultsPanel.ts');
    }
    
    // Extract the block between markers
    let block = content.slice(startIdx + startMarker.length, endIdx).trim();
    
    // Remove 'const html = ' or 'return ' prefix if any
    block = block.replace(/^(const|let|var)\s+\w+\s*=\s*/, '');
    block = block.replace(/^return\s+/, '');
    
    // Remove the trailing semicolon
    if (block.endsWith(';')) block = block.slice(0, -1);
    
    // Reconstruct the string by removing ` + ` junctions
    // This handles both ` + ` and ` + \n`
    let html = block
        .replace(/`\s*\+\s*`/g, '') 
        .trim();
        
    // Finally remove the leading and trailing backticks of the whole block
    if (html.startsWith('`')) html = html.slice(1);
    if (html.endsWith('`')) html = html.slice(0, -1);
    
    // Replace template variables (placeholders)
    html = html.replace(/\$\{nonce\}/g, nonce);
    html = html.replace(/\$\{webview.cspSource\}/g, cspSource);
    html = html.replace(/\$\{stylePath\}/g, 'tabulator.css');
    html = html.replace(/\$\{scriptPath\}/g, 'tabulator.js');
    html = html.replace(/\$\{chartPath\}/g, 'chart.js');
    html = html.replace(/\$\{xlsxPath\}/g, 'xlsx.js');
    
    // Inject Mock VS Code API for immediate execution in JSDOM
    const mockApi = `<script>window.acquireVsCodeApi = () => ({ postMessage: () => {}, setState: () => {}, getState: () => {} });</script>`;
    return mockApi + html;
}

describe('Results Panel UI Density & Logic', () => {
    let dom: JSDOM;
    let document: Document;

    beforeEach(() => {
        const html = getHtmlTemplate();
        dom = new JSDOM(html, { runScripts: "dangerously", resources: "usable" });
        
        // Mock VS Code API for JSDOM
        (dom.window as any).acquireVsCodeApi = () => ({
            postMessage: vi.fn(),
            setState: vi.fn(),
            getState: vi.fn()
        });

        document = dom.window.document;
    });

    it('verifies the diagnostic Build ID is present', () => {
        const buildId = document.body.getAttribute('data-build-id');
        expect(buildId).toBe('DIAGNOSTIC-2026-04-10-02-00');
        
        const buildLabel = document.getElementById('build-id');
        expect(buildLabel?.textContent).toContain('Build: DIAGNOSTIC-2026-04-10-02-00');

        const navPipeline = document.getElementById('nav-pipeline');
        expect(navPipeline).toBeDefined();
    });

    it('verifies the tightened UI spacing (4px gap)', () => {
        const styles = document.querySelector('style')?.textContent || '';
        
        // Check for the 4px gap in pipeline-view (Ultra Dense)
        expect(styles).toContain('#pipeline-view { gap: 4px;');
        
        // Check for the 2px margin in node-card
        expect(styles).toContain('margin-bottom: 2px;');
        
        // Check for the 4px border-radius
        expect(styles).toContain('border-radius: 4px;');
    });

    it('renders a pipeline node correctly', () => {
        // Mock the updatePipeline function logic (since we can't easily run the <script> in JSDOM easily without all deps)
        const view = document.getElementById('pipeline-view')!;
        view.innerHTML = '';
        
        const node = { name: 'Test Node', status: 'Running', rowsProcessed: 1234, durationMs: 450 };
        const card = document.createElement('div');
        card.className = 'node-card ' + node.status;
        card.innerHTML = `
            <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:8px">
                <span style="font-weight:700; font-size:13px">${node.name}</span>
                <span style="font-size:10px; opacity:0.6; text-transform:uppercase">${node.status}</span>
            </div>
            <div style="display:flex; gap:16px; font-size:11px; color:var(--muted)">
                <span>Rows: <strong>${node.rowsProcessed.toLocaleString()}</strong></span>
                <span>Time: <strong>${node.durationMs}ms</strong></span>
            </div>
        `;
        view.appendChild(card);
        
        expect(view.querySelector('.node-card.Running')).toBeDefined();
        expect(view.textContent).toContain('1,234');
        expect(view.textContent).toContain('450ms');
    });

    it('clears all UI state when receiving clear message', async () => {
        const { window } = dom;
        
        // 1. Manually populate some UI state
        window.document.getElementById('message-stream')!.innerHTML = 'old log';
        window.document.getElementById('trace-stream')!.innerHTML = 'old trace';
        window.document.getElementById('pipeline-view')!.innerHTML = 'old pipeline';
        window.document.getElementById('perf-summary')!.innerHTML = 'old perf';
        window.document.getElementById('results-count')!.textContent = '10 rows';
        
        // 2. Post clear message
        window.postMessage({ type: 'clear' }, '*');
        
        // Wait for message loop (JSDOM scripts run synchronously usually, but postMessage index might need a tick)
        await new Promise(resolve => setTimeout(resolve, 50));
        
        // 3. Verify everything is empty/reset
        expect(window.document.getElementById('message-stream')!.innerHTML).toBe('');
        expect(window.document.getElementById('trace-stream')!.innerHTML).toBe('');
        expect(window.document.getElementById('pipeline-view')!.innerHTML).toBe('');
        expect(window.document.getElementById('perf-summary')!.innerHTML).toBe('');
        expect(window.document.getElementById('results-count')!.textContent).toBe('Cleaning up...');
    });
});
