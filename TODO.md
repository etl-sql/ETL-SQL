
## Syntax Additions and Improvements

1. [ ] SET WHAT_IF ON;  This statement will not write anything back to databases, files or any other destination.  It will not send files, create files, directories, or anything else.  It will just run the script and show the user what it would do by writing this out to the messages tab and the results tab.  This is useful for testing scripts before running them in production.  SET WHAT_IF OFF; will turn off the what if mode, this is default.

2. [x] CREATE SSH_KEY_PAIR(<path>, [<bits>], [<algorithm>],  [<passphrase>],  [<comment>]);  This will create a public/private key pair for encrypting and decrypting data.  The passphrase is used to encrypt the private key file.  The bits is the number of bits to use for the key pair.  The path is the path to the directory where the key pair will be stored.  The default path is the same directory as the script file.

3. [x] File Encryption FLATFILE, EXCEL, JSON, XML, PARQUET, AVRO.  Need to add ENCRYPTION algorithm which would be the same as the HASHBYTES funtion has.  Also need to add the ability to encrypt with a SSH key pair and passphrase.  For LINTING when the user has ENCRYPT=ON then they must have a password or a SSH key pair and passphrase.  If they don't have one then linting should fail.

   Documentation already added for FLATFILE:
   - **ALGORITHM**: `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` — algorithm to use for encryption/decryption. (Default: `SHA2_256`)
   - **KEYFILE**: Path to the private key file for public-key authentication. (Required if ENCRYPT=ON)
   - **PASSPHRASE**: The passphrase for the private key file (if any). (Required if ENCRYPT=ON)
   Please update ETL_SQL_Language_Reference.md with these options for EXCEL, JSON, XML, PARQUET, AVRO after implementation.

4. [x] ALTER CONNECTION and CREATE OR ALTER CONNECTION.
   - [x] ALTER token, AlterConnectionStatement AST node, and handler implemented
   - [x] Merge logic for options implemented in CreateConnectionStatementHandler
   - [x] Integration tests added in ConnectionAlterTests.cs
   - [x] Documentation updated in ETL_SQL_Language_Reference.md

## VS CODE Bugs/Improvements

## Doc Review Pending Items

**DR-1. Console Editor (`ui edit`) Command** — *Pending*  
The README describes a full terminal-based editor launched via `dotnet run -- --ui edit MyScript.etlsql`, including shortcuts like `F5`, `Shift+F5`, `Ctrl+I`, `F1`. The language reference has no section for how to launch or use the console editor. Consider adding a "Getting Started / Running Scripts" section to the reference doc.

**DR-2. VS Code Extension** — *Pending*  
The README mentions a dedicated VS Code language server extension. The language reference has no mention of it at all — not even a pointer to where to install it.

**DR-3. Native SQL Pushdown Guide** — *Pending — needs content decision*  
README claims automatic pushdown of joins/filters to source databases. The language reference has no guide explaining when pushdown is triggered, how to force it, or how to prevent it. The `EXECUTE...BEGIN...END` walkthrough touches it informally but there's no clear guide. Add a "Performance / Pushdown" section explaining the rules.
