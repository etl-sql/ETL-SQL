import type { Plugin } from 'vite';

/** Inline Vite's emitted entry assets into the HTML that VS Code loads under its nonce/CSP. */
export function singleFileWebview(): Plugin {
    return {
        name: 'etlsql-single-file-webview',
        enforce: 'post',
        generateBundle(_options, bundle) {
            const inlined = new Set<string>();
            for (const asset of Object.values(bundle)) {
                if (asset.type !== 'asset' || !asset.fileName.endsWith('.html')) continue;
                let html = typeof asset.source === 'string' ? asset.source : new TextDecoder().decode(asset.source);
                html = html.replace(/<script\b([^>]*?)\bsrc="([^"]+)"([^>]*)>\s*<\/script>/gi,
                    (_tag, before: string, url: string, after: string) => {
                        const filename = url.replace(/^\.\//, '');
                        const chunk = bundle[filename];
                        if (chunk?.type !== 'chunk') this.error(`Webview script was not emitted: ${url}`);
                        inlined.add(filename);
                        // All dependencies are inline, so Vite's pending preload list is empty.
                        const code = chunk.code.replace(/"?__VITE_PRELOAD__"?/g, 'void 0')
                            .replace(/<\/script/gi, match => `<\\/${match.slice(2)}`)
                            .replace(/<!--/g, '\\x3c!--');
                        return `<script${before}${after}>${code}</script>`;
                    });
                html = html.replace(/<link\b([^>]*?)\bhref="([^"]+)"([^>]*)>/gi,
                    (tag: string, before: string, url: string, after: string) => {
                        const attributes = before + after;
                        if (!/\brel="stylesheet"/i.test(attributes)) return tag;
                        const filename = url.replace(/^\.\//, '');
                        const css = bundle[filename];
                        if (css?.type !== 'asset') this.error(`Webview stylesheet was not emitted: ${url}`);
                        inlined.add(filename);
                        const source = typeof css.source === 'string' ? css.source : new TextDecoder().decode(css.source);
                        const styleAttributes = attributes.replace(/\brel="stylesheet"/i, '');
                        return `<style${styleAttributes}>${source.replace(/<\/style/gi, match => `<\\/${match.slice(2)}`)}</style>`;
                    });
                asset.source = html;
            }
            // A separate chunk cannot be loaded by the extension's single-file webview contract.
            for (const asset of Object.values(bundle)) {
                if (asset.type === 'chunk' || asset.fileName.endsWith('.css')) {
                    if (!inlined.has(asset.fileName)) this.error(`Webview asset was not inlined: ${asset.fileName}`);
                    delete bundle[asset.fileName];
                }
            }
        },
    };
}
