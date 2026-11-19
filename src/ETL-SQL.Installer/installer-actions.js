// MSI deferred custom actions for ETL-SQL. Deferred actions can only read CustomActionData, so the
// install-folder path is marshaled in via a matching type-51 property (SetWriteJwtData / SetRestoreSettingsData).

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
