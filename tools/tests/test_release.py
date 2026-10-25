"""Publishing failure-path tests; all service calls are mocked, nothing is uploaded."""
import argparse
import base64
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
from zipfile import ZipFile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import release


MANIFEST = {"Version": "0.1.17", "UniqueID": "MossHarbor.ProductionProgress",
            "EntryDll": "ProductionProgress.dll", "UpdateKeys": ["Nexus:54196"]}
NOTES = "English\n- Compatibility fix.\n\n简体中文\n- 修正兼容问题。"


def package(extra=None, manifest=None, duplicate=False):
    data = io.BytesIO()
    with ZipFile(data, "w") as archive:
        for name in sorted(release.PACKAGE_FILES):
            value = b"MZ-test-fixture" if name.endswith(".dll") else b'{"label":"test"}'
            if name.endswith("manifest.json"):
                value = json.dumps(manifest or MANIFEST).encode()
            archive.writestr(name, value)
        if extra:
            archive.writestr(extra, b"not allowed")
        if duplicate:
            archive.writestr("ProductionProgress/manifest.json", json.dumps(MANIFEST))
    return data.getvalue()


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        # CI sets GITHUB_STEP_SUMMARY. Simulated publishing must never write to it.
        summary_patch = patch.object(release, "summary")
        summary_patch.start()
        self.addCleanup(summary_patch.stop)
        self.data = package()
        self.sha = release.digest(self.data)
        self.release = {"id": 10, "draft": True, "prerelease": False,
                        "tag_name": "v0.1.17", "name": "Production Progress 0.1.17", "body": NOTES,
                        "assets": [{"id": 11, "name": "ProductionProgress-0.1.17.zip",
                                    "state": "uploaded", "size": len(self.data)}]}

    def github(self, path, payload=None, binary=False):
        if path == "releases?per_page=100&page=1":
            return [self.release]
        if path.startswith("git/ref/tags/"):
            return {"ref": "refs/tags/v0.1.17", "object": {"type": "tag", "sha": "b" * 40}}
        if path.startswith("git/tags/"):
            return {"object": {"type": "commit", "sha": "a" * 40}}
        if path.startswith("contents/"):
            return {"content": base64.b64encode(json.dumps(MANIFEST).encode()).decode()}
        if path == "releases/assets/11":
            return self.data
        raise AssertionError(f"Unexpected request: {path}")

    def snapshot(self):
        with patch.object(release, "github", side_effect=self.github):
            return release.release_snapshot("v0.1.17", self.sha)[0]

    def nexus(self, path, legacy=False):
        if path == release.NEXUS_MOD:
            return {"data": {"id": "mod-uid", "game_scoped_id": "54196"}}
        if path == "/mods/mod-uid/files":
            return {"data": {"mod_files": [{"id": "file-uid"}]}}
        if path == "/mod-files/file-uid/versions":
            return {"data": {"versions": [{"id": "old", "version": "0.1.16",
                                            "category": "main", "is_primary": True}]}}
        if path == release.NEXUS_MOD + "/changelogs.json":
            return {"0.1.16": ["Old notes"]}
        raise AssertionError(f"Unexpected request: {path}")

    def test_release_zip_allowed(self):
        self.assertEqual(release.validate_package(self.data, "0.1.17", self.sha), MANIFEST)

    def test_wrong_sha_stops(self):
        with self.assertRaisesRegex(ValueError, "SHA-256 不一致"):
            release.validate_package(self.data, "0.1.17", "0" * 64)

    def test_other_mod_or_game_dll_rejected(self):
        for name in ["ProductionProgress/Stardew Valley.dll", "../secret.txt", "config.json"]:
            with self.subTest(name=name):
                data = package(extra=name)
                with self.assertRaises(ValueError):
                    release.validate_package(data, "0.1.17", release.digest(data))

    def test_duplicate_zip_entries_rejected(self):
        with self.assertWarns(UserWarning):
            data = package(duplicate=True)
        with self.assertRaises(ValueError):
            release.validate_package(data, "0.1.17", release.digest(data))

    def test_manifest_version_and_source_must_match(self):
        with self.assertRaises(ValueError):
            release.validate_package(self.data, "0.1.18", self.sha)
        with self.assertRaises(ValueError):
            release.validate_package(self.data, "0.1.17", self.sha, {**MANIFEST, "Author": "different"})

    def test_tags_and_shell_metacharacters_rejected(self):
        for version in ["v0.1.17", "0.1.17-preview", "0.01.17", "0.1.17;echo hi", "../0.1.17"]:
            with self.subTest(version=version), self.assertRaises(ValueError):
                release.version_tuple(version)

    def test_draft_snapshot_includes_review_material(self):
        snapshot = self.snapshot()
        self.assertEqual(snapshot["notes"], NOTES)
        self.assertEqual(snapshot["sha256"], self.sha)
        self.assertEqual(snapshot["source_commit"], "a" * 40)

    def test_draft_lookup_follows_pages_without_published_tag_endpoint(self):
        with patch.object(release, "github", side_effect=[
            [{"tag_name": f"v0.0.{i}"} for i in range(100)], [self.release]
        ]) as api:
            self.assertEqual(release.find_release("v0.1.17"), self.release)
            self.assertEqual([call.args[0] for call in api.call_args_list],
                             ["releases?per_page=100&page=1", "releases?per_page=100&page=2"])

    def test_invisible_or_ambiguous_draft_stops(self):
        for response in [[], [self.release, self.release]]:
            with self.subTest(response=response), patch.object(release, "github", return_value=response):
                with self.assertRaisesRegex(ValueError, "唯一"):
                    release.find_release("v0.1.17")

    def test_published_or_prerelease_does_not_republish(self):
        for key, value in [("draft", False), ("prerelease", True)]:
            previous = self.release[key]
            self.release[key] = value
            with self.assertRaises(ValueError):
                self.snapshot()
            self.release[key] = previous

    def test_extra_github_asset_stops_before_approval(self):
        self.release["assets"].append({"name": "private.log"})
        with self.assertRaises(ValueError):
            self.snapshot()

    def test_draft_changed_while_awaiting_approval(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            snapshot = self.snapshot()
            release.save_json(directory / "release.json", snapshot)
            (directory / snapshot["filename"]).write_bytes(self.data)
            (directory / "changelog.txt").write_text(NOTES)
            self.release["body"] = NOTES + "\nNew unapproved text"
            with patch.object(release, "github", side_effect=self.github), self.assertRaisesRegex(ValueError, "重新确认"):
                release.load_candidate(directory)

    def test_one_existing_main_file_selected(self):
        with patch.object(release, "nexus", side_effect=self.nexus):
            self.assertEqual(release.select_nexus_target(self.snapshot())["file_id"], "file-uid")

    def test_partial_nexus_success_blocks_retry(self):
        for duplicate_file in [True, False]:
            def response(path, legacy=False):
                value = self.nexus(path, legacy)
                if duplicate_file and path.endswith("/versions"):
                    value["data"]["versions"].append({"version": "0.1.17", "category": "archived"})
                if not duplicate_file and path.endswith("changelogs.json"):
                    value["0.1.17"] = NOTES.splitlines()
                return value
            with self.subTest(file=duplicate_file), patch.object(release, "nexus", side_effect=response):
                with self.assertRaisesRegex(ValueError, "同版本"):
                    release.select_nexus_target(self.snapshot())

    def test_downgrade_rejected(self):
        snapshot = self.snapshot()
        snapshot["version"] = "0.1.15"
        with patch.object(release, "nexus", side_effect=self.nexus), self.assertRaisesRegex(ValueError, "更新的正式版本"):
            release.select_nexus_target(snapshot)

    def test_ambiguous_primary_stops(self):
        def response(path, legacy=False):
            value = self.nexus(path, legacy)
            if path.endswith("/versions"):
                value["data"]["versions"] *= 2
            return value
        with patch.object(release, "nexus", side_effect=response), self.assertRaisesRegex(ValueError, "只有一个"):
            release.select_nexus_target(self.snapshot())

    def test_nexus_failure_never_publishes_github(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            release.save_json(directory / "target.json", {"file_id": "file-uid"})
            with patch.object(release, "load_candidate", return_value=self.snapshot()), \
                 patch.object(release, "verify_nexus", side_effect=ValueError("Not synced")), \
                 patch.object(release.time, "sleep"), patch.object(release, "github") as publish:
                with self.assertRaisesRegex(ValueError, "Not synced"):
                    release.finish(argparse.Namespace(directory=directory, version_id="new"))
                publish.assert_not_called()

    def test_verify_all_nexus_fields_not_just_upload_success(self):
        snapshot = self.snapshot()
        target = {"file_id": "file-uid", "previous_version_id": "old"}
        def response(path, legacy=False):
            if path.endswith("/versions"):
                return {"data": {"versions": [
                    {"id": "new", "version": "0.1.17", "category": "main", "is_primary": True},
                    {"id": "old", "version": "0.1.16", "category": "archived"}]}}
            if path.endswith("changelogs.json"):
                return {"0.1.17": NOTES.splitlines()}
            return {"version": "0.1.17"}
        with patch.object(release, "nexus", side_effect=response):
            release.verify_nexus(snapshot, target, "new")
        def missing_notes(path, legacy=False):
            return {} if path.endswith("changelogs.json") else response(path, legacy)
        with patch.object(release, "nexus", side_effect=missing_notes), self.assertRaisesRegex(ValueError, "更新日志"):
            release.verify_nexus(snapshot, target, "new")
        def stale_page(path, legacy=False):
            return {"version": "0.1.16"} if path.endswith("54196.json") else response(path, legacy)
        with patch.object(release, "nexus", side_effect=stale_page), self.assertRaisesRegex(ValueError, "页面版本"):
            release.verify_nexus(snapshot, target, "new")

    def test_nexus_verified_before_github_is_published(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            release.save_json(directory / "target.json", {"file_id": "file-uid"})
            order = []
            def publish(path, payload=None):
                order.append("publish")
                self.assertEqual(payload["draft"], False)
                return {"draft": False, "tag_name": "v0.1.17", "html_url": "https://github.com/shy-ci/ProductionProgress/releases/tag/v0.1.17"}
            snapshot = self.snapshot()
            def submit(*args):
                order.append("curseforge")
                return {"status": "submitted", "project_id": 1755780, "sha256": snapshot["sha256"],
                        "notes_sha256": snapshot["notes_sha256"], "version": "0.1.17", "file_id": 123,
                        "game_versions": ["1.6.8", "1.6.15"]}
            with patch.object(release, "load_candidate", return_value=snapshot), \
                 patch.object(release, "verify_nexus", side_effect=lambda *args: order.append("verify")), \
                 patch.object(release.curseforge, "submit", side_effect=submit), \
                 patch.object(release, "github", side_effect=publish):
                release.finish(argparse.Namespace(directory=directory, version_id="new"))
            self.assertEqual(order, ["verify", "curseforge", "publish"])

    def test_curseforge_failure_keeps_github_draft(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            release.save_json(directory / "target.json", {"file_id": "file-uid"})
            with patch.object(release, "load_candidate", return_value=self.snapshot()), \
                 patch.object(release, "verify_nexus"), \
                 patch.object(release.curseforge, "submit", side_effect=ValueError("CF response unknown")), \
                 patch.object(release, "github") as publish:
                with self.assertRaisesRegex(ValueError, "CF response unknown"):
                    release.finish(argparse.Namespace(directory=directory, version_id="new"))
                publish.assert_not_called()

    def test_connection_check_only_reads_the_expected_account(self):
        def response(path, legacy=False):
            if path == "/users/validate.json":
                return {"user_id": 237909389}
            if path.endswith("54196.json"):
                return {"version": "0.1.16"}
            return self.nexus(path, legacy)
        with patch.object(release, "nexus", side_effect=response), patch.object(release, "github") as github, \
             patch.object(release.curseforge, "preflight", return_value={"game_version_ids": [1234, 1236]}):
            release.connection(None)
            github.assert_not_called()
        with patch.object(release, "nexus", return_value={"user_id": 1}), self.assertRaisesRegex(ValueError, "既有 Nexus 发布账户"):
            release.connection(None)


if __name__ == "__main__":
    unittest.main()
