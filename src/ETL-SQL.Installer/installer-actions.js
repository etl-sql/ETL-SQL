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

// Immediate action run during an interactive uninstall: asks (OS-level Yes/No message box, so it
// works even when Settings > Apps runs the uninstall in basic UI) whether to also delete runtime
// data, and records the answer in CLEANDATA. Gated by sequence condition to skip silent uninstalls.
function PromptCleanData() {
    try {
        var shell = new ActiveXObject("WScript.Shell");
        // ASCII only — the JScript engine reads this Binary as ANSI, so non-ASCII (em dashes) mojibake.
        // type = MB_YESNO(4) | MB_ICONEXCLAMATION(0x30) | MB_DEFBUTTON2(0x100, No default)
        //      | MB_SETFOREGROUND(0x10000) | MB_TOPMOST(0x40000) so it pops in front of the installer.
        var response = shell.Popup(
            "Also delete all ETL-SQL data (reports, database, snapshots, logs)?\n\n"
            + "Choose No to keep your data (recommended) - it will survive a reinstall or upgrade.\n"
            + "Choosing Yes permanently deletes it and cannot be undone.",
            0, "Uninstall ETL-SQL", 4 + 0x30 + 0x100 + 0x10000 + 0x40000);
        Session.Property("CLEANDATA") = (response == 6) ? "1" : "0"; // 6 = IDYES
    } catch (e) {
        Session.Property("CLEANDATA") = "0";
    }
    return 1;
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
