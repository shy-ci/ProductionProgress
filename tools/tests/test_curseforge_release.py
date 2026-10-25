import email
from email import policy
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from urllib.error import HTTPError
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import curseforge_release as cf
import release
from test_release import NOTES, package


class CurseForgeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.data = package()
        self.snapshot = {"version": "0.1.17", "filename": "ProductionProgress-0.1.17.zip",
                         "sha256": release.digest(self.data), "notes": NOTES,
                         "notes_sha256": release.digest(NOTES.encode())}
        self.target = {"project_id": cf.PROJECT_ID, "page": cf.PAGE,
                       "game_versions": ["1.6.8", "1.6.15"], "game_version_ids": [1234, 1236]}
        (self.directory / self.snapshot["filename"]).write_bytes(self.data)
        (self.directory / "curseforge-target.json").write_text(json.dumps(self.target))

    def test_all_confirmed_game_versions_required(self):
        versions = [{"id": 1236, "name": "1.6.15"}, {"id": 1234, "name": "1.6.8"}, {"id": 1235, "name": "1.6.9"}]
        with patch.object(cf, "request", return_value=versions):
            self.assertEqual(cf.preflight(), self.target)
        for response in [[], {}, versions[:1], versions[1:], versions + [versions[0]],
                         [{"id": 1, "name": name} for name in cf.GAME_VERSIONS]]:
            with self.subTest(response=response), patch.object(cf, "request", return_value=response), self.assertRaises(cf.UploadError):
                cf.preflight()

    def test_multipart_contains_exact_verified_zip_and_notes(self):
        details = cf.metadata(self.snapshot, self.target)
        body, content_type = cf.multipart(self.data, self.snapshot["filename"], details)
        parsed = email.message_from_bytes(f"Content-Type: {content_type}\r\nMIME-Version: 1.0\r\n\r\n".encode() + body, policy=policy.default)
        parts = list(parsed.iter_parts())
        self.assertEqual(parts[1].get_payload(decode=True), self.data)
        self.assertEqual(parts[1].get_filename(), self.snapshot["filename"])
        metadata = json.loads(parts[0].get_payload(decode=True))
        self.assertEqual(metadata["changelog"], NOTES)
        self.assertEqual(metadata["displayName"], "Production Progress 0.1.17")
        self.assertEqual(metadata["releaseType"], "release")
        self.assertEqual(metadata["gameVersions"], [1234, 1236])
        self.assertFalse(metadata["isMarkedForManualRelease"])

    def test_submission_is_not_claimed_as_publication(self):
        with patch.object(cf, "request", return_value={"id": 123}) as upload:
            receipt = cf.submit(self.directory, self.snapshot, release.validate_package)
        self.assertEqual(upload.call_count, 1)
        self.assertEqual(receipt["status"], "submitted")
        self.assertEqual(receipt["publication_status"], "not-verified")
        self.assertEqual(receipt["file_id"], 123)
        cf.verify_receipt(receipt, self.snapshot)

    def test_wrong_target_is_never_uploaded(self):
        self.target["project_id"] = 1
        (self.directory / "curseforge-target.json").write_text(json.dumps(self.target))
        with patch.object(cf, "request") as upload, self.assertRaises(cf.UploadError):
            cf.submit(self.directory, self.snapshot, release.validate_package)
        upload.assert_not_called()

    def test_partial_or_invalid_version_selection_is_never_uploaded(self):
        for ids in [[1234], [1234, 1234], [1234, True], [1234, -1]]:
            target = {**self.target, "game_version_ids": ids}
            (self.directory / "curseforge-target.json").write_text(json.dumps(target))
            with self.subTest(ids=ids), patch.object(cf, "request") as upload, self.assertRaises(cf.UploadError):
                cf.submit(self.directory, self.snapshot, release.validate_package)
            upload.assert_not_called()

    def test_unconfirmed_game_version_is_rejected(self):
        target = {**self.target, "game_versions": ["1.6.8", "future-version"]}
        with self.assertRaises(cf.UploadError):
            cf.metadata(self.snapshot, target)

    def test_changed_package_is_never_uploaded(self):
        (self.directory / self.snapshot["filename"]).write_bytes(b"changed")
        with patch.object(cf, "request") as upload, self.assertRaises(ValueError):
            cf.submit(self.directory, self.snapshot, release.validate_package)
        upload.assert_not_called()

    def test_uncertain_response_is_recorded_without_retry(self):
        with patch.object(cf, "request", side_effect=cf.UploadError("timeout")) as upload:
            with self.assertRaises(cf.UploadError):
                cf.submit(self.directory, self.snapshot, release.validate_package)
            self.assertEqual(upload.call_count, 1)
            receipt = json.loads((self.directory / "curseforge.json").read_text())
            self.assertEqual(receipt["status"], "outcome-unknown")
            with self.assertRaisesRegex(cf.UploadError, "不重复上传"):
                cf.submit(self.directory, self.snapshot, release.validate_package)
            self.assertEqual(upload.call_count, 1)

    def test_missing_file_id_is_not_success(self):
        for response in [{}, {"id": None}, {"id": -1}, {"id": True}]:
            with self.subTest(response=response):
                (self.directory / "curseforge.json").unlink(missing_ok=True)
                with patch.object(cf, "request", return_value=response), self.assertRaises(cf.UploadError):
                    cf.submit(self.directory, self.snapshot, release.validate_package)
                self.assertEqual(json.loads((self.directory / "curseforge.json").read_text())["status"], "outcome-unknown")

    def test_receipt_must_match_approved_candidate(self):
        with patch.object(cf, "request", return_value={"id": 123}):
            receipt = cf.submit(self.directory, self.snapshot, release.validate_package)
        receipt["sha256"] = "0" * 64
        with self.assertRaises(cf.UploadError):
            cf.verify_receipt(receipt, self.snapshot)

    def test_missing_token_stops_before_network(self):
        with patch.dict(os.environ, {"CURSEFORGE_API_TOKEN": ""}), patch.object(cf, "build_opener") as network:
            with self.assertRaisesRegex(cf.UploadError, "未配置"):
                cf.preflight()
            network.assert_not_called()

    def test_token_stays_in_header_and_errors_do_not_echo_it(self):
        with patch.dict(os.environ, {"CURSEFORGE_API_TOKEN": "test-token-not-a-real-secret"}), \
             patch.object(cf, "build_opener") as opener:
            opener.return_value.open.side_effect = HTTPError("ignored", 403, "token could be echoed here", {}, None)
            with self.assertRaises(cf.UploadError) as error:
                cf.preflight()
            request = opener.return_value.open.call_args.args[0]
            self.assertEqual(request.full_url, cf.API + "/game/stardewvalley/versions")
            self.assertEqual(request.get_header("X-api-token"), "test-token-not-a-real-secret")
            self.assertNotIn("test-token", str(error.exception))
            self.assertEqual(opener.return_value.open.call_count, 1)


if __name__ == "__main__":
    unittest.main()
