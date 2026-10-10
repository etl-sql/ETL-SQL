// Copyright 2026 Charles Clemens and ETL-SQL contributors. Apache-2.0.
// Our local packaging toolchain creates unsigned archives. It does not install Microsoft's
// proprietary signer. Never claim that a signature was produced or verified.
const unsupported = () => { throw new Error('VSIX signing is not available in the FOSS packaging toolchain.'); };
module.exports = { generateManifest: unsupported, verify: unsupported, zip: unsupported };
