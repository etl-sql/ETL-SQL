import { defineConfig } from 'vitest/config';
import path from 'path';

export default defineConfig({
    test: {
        globals: true,
        environment: 'node',
        include: ['src/test/**/*.test.ts'],
        coverage: {
            provider: 'v8',
            reporter: ['text', 'html'],
            include: ['src/**/*.ts'],
            exclude: ['src/test/**', 'src/__mocks__/**']
        }
    },
    resolve: {
        alias: {
            // Redirect the vscode built-in to our mock
            vscode: path.resolve(__dirname, 'src/__mocks__/vscode.ts')
        }
    }
});
