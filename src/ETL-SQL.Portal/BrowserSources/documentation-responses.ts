import { isObject } from './http-error.js';

function isSearchResult(value: unknown): value is DocumentationSearchResult {
    return isObject(value) && typeof value.path === 'string' && typeof value.title === 'string'
        && typeof value.section === 'string' && typeof value.snippet === 'string' && typeof value.score === 'number';
}

export function readDocumentationSearch(value: unknown): DocumentationSearchResult[] {
    if (!Array.isArray(value) || !value.every(isSearchResult)) {
        throw new TypeError('Invalid documentation search response.');
    }
    return value;
}

export function readDocumentationDocument(value: unknown): DocumentationDocument {
    if (!isObject(value) || typeof value.path !== 'string' || typeof value.title !== 'string' || typeof value.markdown !== 'string') {
        throw new TypeError('Invalid documentation document response.');
    }
    return value as unknown as DocumentationDocument;
}
