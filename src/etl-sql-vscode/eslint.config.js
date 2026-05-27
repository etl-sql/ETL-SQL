// @ts-check
const js = require('@eslint/js');
const tsPlugin = require('@typescript-eslint/eslint-plugin');
const tsParser = require('@typescript-eslint/parser');
const globals = require('globals');

/** @type {import('eslint').Linter.Config[]} */
module.exports = [
    {
        ignores: ['out/**', 'dist/**', '**/*.d.ts']
    },
    js.configs.recommended,
    {
        files: ['src/**/*.ts'],
        languageOptions: {
            parser: tsParser,
            parserOptions: {
                ecmaVersion: 2022,
                sourceType: 'module'
            },
            globals: {
                ...globals.node,
                ...globals.es2022
            }
        },
        plugins: {
            '@typescript-eslint': tsPlugin
        },
        rules: {
            ...tsPlugin.configs.recommended.rules,
            'no-undef': 'off',
            '@typescript-eslint/naming-convention': [
                'warn',
                { selector: 'import', format: ['camelCase', 'PascalCase'] }
            ],
            '@typescript-eslint/no-explicit-any': 'warn',
            '@typescript-eslint/no-unused-vars': 'warn',
            'curly': 'warn',
            'eqeqeq': 'warn',
            'no-throw-literal': 'warn',
            'semi': 'warn'
        }
    }
];
