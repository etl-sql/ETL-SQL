// MSI deferred custom actions for ETL-SQL. Deferred actions can only read CustomActionData, so the
// install-folder path is marshaled in via a matching type-51 property (SetWriteJwtData / SetCleanDataDir).

// Runs configure-portal-jwt.ps1 from the install folder (generates the JWT secret + approves the
// install folder as a security safe zone) before the services start.
function ConfigureJwt() {
    try {
        var customData = Session.Property("CustomActionData");
        if (!customData) {
            return 3;
        }
        var installDir = customData.split("|")[0];
        var shell = new ActiveXObject("WScript.Shell");
        var cmd = 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'
                + installDir + 'configure-portal-jwt.ps1" -CustomData "' + customData.replace(/"/g, '""') + '"';
        return shell.Run(cmd, 0, true) === 0 ? 1 : 3; // hidden window, wait for completion
    } catch (e) {
        return 3; // msiDoActionStatusFailure; never include settings or secrets in diagnostics.
    }
}

// A major upgrade's RemoveExistingProducts deletes the installed appsettings.json before the new one is
// laid down, and it must run straight after InstallValidate or InstallInitialize (error 2613 otherwise),
// so no deferred action can copy the file first. This immediate action reads it into the hidden
// PREVIOUS_SETTINGS property instead; RestoreSettings writes it back after InstallFiles, and
// configure-portal-jwt.ps1 carries its values forward. UTF-8 through ADODB.Stream: the file is not ANSI.
function ReadSettings() {
    try {
        var path = Session.Property("INSTALLFOLDER") + "appsettings.json";
        var fso = new ActiveXObject("Scripting.FileSystemObject");
        if (!fso.FileExists(path)) {
            return 1;
        }
        var stream = new ActiveXObject("ADODB.Stream");
        stream.Type = 2; // text
        stream.Charset = "utf-8";
        stream.Open();
        stream.LoadFromFile(path);
        Session.Property("PREVIOUS_SETTINGS") = stream.ReadText();
        stream.Close();
    } catch (e) {
        return 3;
    }
    return 1;
}

// Deferred: CustomActionData is "<install folder>|<previous appsettings.json text>".
function RestoreSettings() {
    try {
        var data = Session.Property("CustomActionData");
        var split = data ? data.indexOf("|") : -1;
        if (split < 0 || split === data.length - 1) {
            return 3;
        }
        var stream = new ActiveXObject("ADODB.Stream");
        stream.Type = 2;
        stream.Charset = "utf-8";
        stream.Open();
        stream.WriteText(data.substring(split + 1));
        stream.SaveToFile(data.substring(0, split) + "appsettings.pre-upgrade.json", 2); // overwrite
        stream.Close();
    } catch (e) {
        return 3;
    }
    return 1;
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
                     "etlsql.db", "etlsql.db-wal", "etlsql.db-shm",
                     "appsettings.secrets.json", "first-run-password.txt"];
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
