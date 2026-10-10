import path from 'node:path';

// Child package scripts and VSCE's npm closure check must use the same Node/npm distribution.
// Windows environment keys are case-insensitive; remove duplicate spellings without changing the caller.
export function nodeCommandEnvironment() {
    const environment = { ...process.env };
    let originalPath = '';
    for (const key of Object.keys(environment)) if (key.toLowerCase() === 'path') {
        originalPath = environment[key];
        delete environment[key];
    }
    environment.PATH = path.dirname(process.execPath) + path.delimiter + originalPath;
    return environment;
}
