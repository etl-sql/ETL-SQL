#!/usr/bin/env python3
"""Run the DEB's authored configuration Python on private POSIX files; never install a package."""
import copy
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest


SOURCE = Path(__file__).resolve().parents[2] / "scripts/build-linux-packages.sh"
MATCH = re.search(r'python3 - "\$CFG".*?<<\'PY\'\n(.*?)\nPY', SOURCE.read_text(), re.S)
if MATCH is None:
    raise RuntimeError("Missing DEB configuration program")
# The disposable image has no installed service account. Supply only its group lookup;
# actual Linux open/chown/chmod/replace operations and configuration logic execute unchanged.
PRELUDE = "import grp\ngrp.getgrnam = lambda name: grp.struct_group(('etlsql', 'x', 1654, []))\n"
DEFAULTS = {
    "Portal": {"Jwt": {"Secret": "", "PreviousSecrets": []},
               "Dataset": {"AtRestKey": "", "PreviousAtRestKeys": []},
               "FirstRun": {"AdminUsername": "admin", "AdminPassword": ""},
               "Orchestrator": {"ApiKey": "", "IdentitySigningSecret": ""}},
    "Orchestrator": {"ApiKey": "", "IdentitySigningSecret": "", "PreviousApiKeys": []},
    "Security": {"ApprovedSafeZones": []},
}


class LinuxInstallerConfigTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="etlsql-linux-config-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.config = self.root / "appsettings.json"
        self.secrets = self.root / "appsettings.secrets.json"
        self.first_run = self.root / "first-run-password"
        self.program = self.root / "configure.py"
        self.program.write_text(PRELUDE + MATCH.group(1))
        self.config.write_text(json.dumps(copy.deepcopy(DEFAULTS)))

    def run_config(self, previous="", fail_replace=None):
        program = self.program
        if fail_replace is not None:
            program = self.root / "configure-failure.py"
            # Inject an I/O failure at the actual rename boundary, without changing configuration logic.
            fault = ("import os\n_original_replace = os.replace\n"
                     "def _replace(source, target):\n"
                     f"    if target == {str(fail_replace)!r}:\n"
                     "        raise PermissionError('synthetic rename failure')\n"
                     "    return _original_replace(source, target)\n"
                     "os.replace = _replace\n")
            program.write_text(PRELUDE + fault + MATCH.group(1))
        return subprocess.run([sys.executable, str(program), str(self.config),
                               str(self.root / "saved.json"), str(self.first_run), previous],
                              capture_output=True, text=True, timeout=10)

    def assert_sign_in_matches(self):
        private = json.loads(self.secrets.read_text())
        self.assertTrue(self.first_run.read_text().splitlines()[1] == private["Portal"]["FirstRun"]["AdminPassword"],
                        "Sign-in credentials do not match the configured password")
        self.assertEqual(self.first_run.stat().st_mode & 0o777, 0o600)

    def test_failed_secret_write_does_not_publish_unusable_sign_in(self):
        self.assertNotEqual(self.run_config(fail_replace=self.secrets).returncode, 0)
        self.assertFalse(self.first_run.exists(), "Failed configuration published unusable credentials")
        self.assertEqual(self.run_config().returncode, 0)
        self.assert_sign_in_matches()

    def test_failed_sign_in_write_can_retry_with_persisted_keys(self):
        self.first_run.mkdir()
        self.assertNotEqual(self.run_config().returncode, 0)
        self.first_run.rmdir()
        private = json.loads(self.secrets.read_text())
        self.assertEqual(self.run_config().returncode, 0)
        self.assertTrue(private == json.loads(self.secrets.read_text()), "Sign-in recovery replaced installed keys")
        self.assert_sign_in_matches()

    def test_failed_shared_write_can_retry_without_publishing_credentials_or_replacing_keys(self):
        shared = self.config.read_bytes()
        self.assertNotEqual(self.run_config(fail_replace=self.config).returncode, 0)
        self.assertFalse(self.first_run.exists(), "Failed configuration published credentials")
        self.assertTrue(self.config.read_bytes() == shared, "Failed replacement changed shared settings")
        private = json.loads(self.secrets.read_text())
        self.assertEqual(self.run_config().returncode, 0)
        self.assertTrue(private == json.loads(self.secrets.read_text()), "Settings recovery replaced installed keys")
        self.assert_sign_in_matches()

    def test_fresh_generates_protected_keys(self):
        result = self.run_config()
        self.assertEqual(result.returncode, 0, "Fresh configuration failed")
        private = json.loads(self.secrets.read_text())
        self.assertTrue(bool(private["Portal"]["Jwt"]["Secret"]))
        self.assertTrue(bool(private["Portal"]["Dataset"]["AtRestKey"]))
        self.assertTrue(private["Orchestrator"]["ApiKey"] == private["Portal"]["Orchestrator"]["ApiKey"])
        self.assertEqual(self.secrets.stat().st_mode & 0o777, 0o640)
        self.assertEqual((self.secrets.stat().st_uid, self.secrets.stat().st_gid), (0, 1654))
        self.assertEqual(self.first_run.stat().st_mode & 0o777, 0o600)
        self.assertFalse(bool(json.loads(self.config.read_text())["Portal"]["Jwt"]["Secret"]))

    def test_fresh_sign_in_preserves_configured_unicode_credentials(self):
        data = copy.deepcopy(DEFAULTS)
        data["Portal"]["FirstRun"]["AdminUsername"] = "synthetic-admin-\u00e9"
        data["Portal"]["FirstRun"]["AdminPassword"] = "synthetic-password-\u03b1-9!"
        self.config.write_text(json.dumps(data))
        self.assertEqual(self.run_config().returncode, 0)
        self.assertTrue(self.first_run.read_text().splitlines()[0] == data["Portal"]["FirstRun"]["AdminUsername"],
                        "Sign-in file changed the configured username")
        self.assert_sign_in_matches()

    def test_upgrade_keeps_operator_secrets_and_generated_keys(self):
        self.assertEqual(self.run_config().returncode, 0)
        private = json.loads(self.secrets.read_text())
        private["Smtp"] = {"Password": "synthetic-mail-password"}
        private["ConnectionStrings"] = {"Operator": "synthetic-database-password"}
        private["Portal"]["CustomPrivate"] = {"Values": ["keep", None, "exact"]}
        self.secrets.write_text(json.dumps(private))
        self.assertEqual(self.run_config("0.21.0").returncode, 0)
        self.assertTrue(private == json.loads(self.secrets.read_text()), "Protected configuration changed")
        shared = self.config.read_text()
        self.assertNotIn("synthetic-mail-password", shared)
        self.assertNotIn("synthetic-database-password", shared)
        self.assertNotIn("CustomPrivate", shared)

    def test_invalid_shared_or_saved_document_preserves_installed_secrets(self):
        for name in ("appsettings.json", "saved.json"):
            for document in ("null", "[{}]"):
                with self.subTest(name=name, document=document):
                    self.config.write_text(json.dumps(copy.deepcopy(DEFAULTS)))
                    (self.root / "saved.json").unlink(missing_ok=True)
                    self.assertEqual(self.run_config("0.21.0").returncode, 0)
                    private = self.secrets.read_bytes()
                    invalid = self.root / name
                    invalid.write_text(document)
                    shared = self.config.read_bytes()
                    self.assertNotEqual(self.run_config("0.21.0").returncode, 0)
                    self.assertTrue(self.secrets.read_bytes() == private, "Protected configuration changed")
                    self.assertTrue(self.config.read_bytes() == shared, "Shared configuration changed")
                    self.assertEqual(invalid.read_text(), document)


class LinuxInstallerEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="etlsql-linux-evidence-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.root.chmod(0o755)
        self.report = self.root / "release-validation/deb/installed/recovery-report.json"
        self.report.parent.mkdir(parents=True)
        workflow = (SOURCE.parent.parent / ".github/workflows/deb-upgrade.yml").read_text()
        step = re.search(r"      - name: Prepare installation diagnostics\n(.*?)(?=      - name:)", workflow, re.S)
        self.assertIsNotNone(step, "DEB diagnostics must be prepared before artifact upload")
        self.assertIn("if: always()", step.group(1))
        self.program = "\n".join(line[10:] for line in step.group(1).split("        run: |\n", 1)[1].splitlines())

    def stage(self):
        # Only sudo and the runner identity are supplied. Real Linux test/chown operations execute.
        prelude = 'sudo() { "$@"; }\nid() { printf "65534\\n"; }\n'
        return subprocess.run(["bash", "-c", prelude + self.program], cwd=self.root,
                              capture_output=True, text=True)

    def runner_read(self, path):
        return subprocess.run([sys.executable, "-c", "from pathlib import Path; import sys; Path(sys.argv[1]).read_bytes()", str(path)],
                              cwd=self.root, user=65534, group=65534, capture_output=True)

    def test_only_report_becomes_readable_by_runner(self):
        self.report.write_text('{"synthetic":"diagnostic report"}')
        self.report.chmod(0o600)
        backup = self.report.parent / "fresh-backup/etl-sql-keys-fixture.zip"
        backup.parent.mkdir()
        backup.write_bytes(b"synthetic protected backup")
        backup.chmod(0o600)
        original = self.report.read_bytes()
        self.assertNotEqual(self.runner_read(self.report).returncode, 0)
        result = self.stage()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.runner_read(self.report).returncode, 0)
        self.assertEqual(self.report.read_bytes(), original)
        self.assertEqual(self.report.stat().st_uid, 65534)
        self.assertEqual(self.report.stat().st_mode & 0o777, 0o600)
        self.assertEqual(backup.stat().st_uid, 0)
        self.assertEqual(backup.stat().st_mode & 0o777, 0o600)
        self.assertNotEqual(self.runner_read(backup).returncode, 0)

    def test_missing_report_preserves_earlier_diagnostics(self):
        diagnostic = self.report.parent / "summary.txt"
        diagnostic.write_text("synthetic earlier failure")
        self.assertEqual(self.stage().returncode, 0)
        self.assertEqual(diagnostic.stat().st_uid, 0)

    def test_linked_report_cannot_change_target_ownership(self):
        target = self.root / "protected.json"
        target.write_text("synthetic protected data")
        self.report.symlink_to(target)
        self.assertNotEqual(self.stage().returncode, 0)
        self.assertEqual(target.stat().st_uid, 0)

    def test_broken_link_is_rejected(self):
        self.report.symlink_to(self.root / "missing.json")
        self.assertNotEqual(self.stage().returncode, 0)

    def test_directory_is_rejected(self):
        self.report.mkdir()
        self.assertNotEqual(self.stage().returncode, 0)


def invalid_document_test(document):
    def test(self):
        self.secrets.write_text(document)
        original = self.config.read_bytes()
        result = self.run_config()
        self.assertNotEqual(result.returncode, 0, "Invalid protected document accepted")
        self.assertTrue(self.secrets.read_text() == document, "Invalid protected document replaced")
        self.assertTrue(self.config.read_bytes() == original, "Shared configuration changed")
        self.assertFalse(self.first_run.exists(), "Failed configuration created sign-in credentials")
    return test


for name, document in [("array", "[]"), ("null", "null"), ("string", '"synthetic-invalid-document"'),
                       ("number", "42"), ("boolean", "true"), ("singleton_array", "[{}]"),
                       ("empty", ""), ("malformed", '{"synthetic-invalid-document":')]:
    setattr(LinuxInstallerConfigTests, "test_rejects_" + name, invalid_document_test(document))

if __name__ == "__main__":
    if os.name != "posix" or os.getuid() != 0:
        raise SystemExit("Run as root inside a disposable Linux test container")
    unittest.main(verbosity=2)
