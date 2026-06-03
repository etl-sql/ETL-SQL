// MSI deferred custom actions for ETL-SQL. Deferred actions can only read CustomActionData, so the
// install-folder path is marshaled in via a matching type-51 property (SetWriteJwtData / SetCleanDataDir).

// Runs configure-portal-jwt.ps1 from the install folder (generates the JWT secret + approves the
// install folder as a security safe zone) before the services start.
function ConfigureJwt() {
    try {
        var installDir = Session.Property("CustomActionData");
        if (!installDir) {
            return 1;
        }
        var shell = new ActiveXObject("WScript.Shell");
        var cmd = 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'
                + installDir + 'configure-portal-jwt.ps1"';
        shell.Run(cmd, 0, true); // hidden window, wait for completion
    } catch (e) {
        // Non-fatal — the services still install; a missing secret surfaces at service start.
    }
    return 1; // msiDoActionStatusSuccess
}

// Deletes runtime data left under the install folder when the user opts in (CLEANDATA=1) during an
// uninstall. The MSI does not track these (they are created at runtime), so they must be removed here.
function CleanData() {
    try {
        var installDir = Session.Property("CustomActionData");
        if (!installDir) {
            return 1;
        }
        var fso = new ActiveXObject("Scripting.FileSystemObject");
        var folders = ["logs", "Snapshots", "data", "Reports"];
        for (var i = 0; i < folders.length; i++) {
            var dir = installDir + folders[i];
            if (fso.FolderExists(dir)) {
                try { fso.DeleteFolder(dir, true); } catch (eFolder) {}
            }
        }
        var files = ["portal.db", "portal.db-wal", "portal.db-shm",
                     "etlsql.db", "etlsql.db-wal", "etlsql.db-shm"];
        for (var j = 0; j < files.length; j++) {
            var file = installDir + files[j];
            if (fso.FileExists(file)) {
                try { fso.DeleteFile(file, true); } catch (eFile) {}
            }
        }
    } catch (e) {
        // Non-fatal — leftover data is harmless; never fail an uninstall over cleanup.
    }
    return 1; // msiDoActionStatusSuccess
}
