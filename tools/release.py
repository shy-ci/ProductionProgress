"""Prepare and verify a human-approved GitHub -> Nexus release. No game code runs."""
import argparse
import base64
import hashlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.parse import quote
from urllib.request import Request, urlopen
from zipfile import BadZipFile, ZipFile

REPOSITORY = "shy-ci/ProductionProgress"
NEXUS_PAGE = "https://www.nexusmods.com/stardewvalley/mods/54196"
NEXUS_MOD = "/games/stardewvalley/mods/54196"
PACKAGE_FILES = {
    "ProductionProgress/ProductionProgress.dll",
    "ProductionProgress/ProductionProgress.Core.dll",
    "ProductionProgress/manifest.json",
    "ProductionProgress/i18n/default.json",
    "ProductionProgress/i18n/zh.json",
}
MAX_PACKAGE_SIZE = 100 * 1024 * 1024


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def version_tuple(version):
    require(re.fullmatch(r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)", version),
            "正式版本必须为 X.Y.Z，例如 0.1.17。")
    return tuple(map(int, version.split(".")))


def validate_package(data, version, expected_sha, source_manifest=None):
    version_tuple(version)
    require(re.fullmatch(r"[a-f0-9]{64}", expected_sha), "请提供本地验收包的 SHA-256。")
    require(len(data) <= MAX_PACKAGE_SIZE, "安装包超过本流程的 100 MiB 限制。")
    require(digest(data) == expected_sha, "ZIP 与确认的 SHA-256 不一致。")
    with ZipFile(io.BytesIO(data)) as archive:
        entries = archive.infolist()
        require(len(entries) == len(PACKAGE_FILES) and
                {entry.filename for entry in entries} == PACKAGE_FILES,
                "ZIP 必须且只能包含两个自有 DLL、manifest 及中英文翻译。")
        require(sum(entry.file_size for entry in entries) <= MAX_PACKAGE_SIZE,
                "解压内容过大。")
        require(archive.testzip() is None, "ZIP 完整性检查失败。")
        manifest = json.loads(archive.read("ProductionProgress/manifest.json"))
        require(manifest["Version"] == version, "ZIP 内版本与发布标签不一致。")
        require(manifest["UniqueID"] == "MossHarbor.ProductionProgress" and
                manifest["EntryDll"] == "ProductionProgress.dll" and
                manifest["UpdateKeys"] == ["Nexus:54196"], "ZIP 并非目标 mod。")
        if source_manifest is not None:
            require(manifest == source_manifest, "ZIP manifest 与源码标签不一致。")
        for name in PACKAGE_FILES:
            content = archive.read(name)
            require(bool(content), "安装包包含空文件。")
            if name.endswith(".dll"):
                require(content.startswith(b"MZ"), "DLL 文件头无效。")
        english = json.loads(archive.read("ProductionProgress/i18n/default.json"))
        chinese = json.loads(archive.read("ProductionProgress/i18n/zh.json"))
        require(english.keys() == chinese.keys(), "中英文翻译键不一致。")
    return manifest


def github(path, payload=None, binary=False):
    args = ["gh", "api", f"repos/{REPOSITORY}/{path}", "-H",
            "Accept: application/octet-stream" if binary else "Accept: application/vnd.github+json"]
    if payload is not None:
        args += ["--method", "PATCH", "--input", "-"]
    result = subprocess.run(args, input=json.dumps(payload).encode() if payload is not None else None,
                            capture_output=True, check=False)
    require(result.returncode == 0,
            f"GitHub 请求失败：{path}（exit {result.returncode}）；请检查权限/网络及远端状态，未自动重试。")
    return result.stdout if binary else json.loads(result.stdout)


def nexus(path, legacy=False):
    key = os.environ.get("NEXUSMODS_API_KEY", "").strip()
    require(bool(key), "production-release 环境未配置 NEXUSMODS_API_KEY。")
    url = "https://api.nexusmods.com/" + ("v1" if legacy else "v3") + path
    request = Request(url, headers={"apikey": key, "Accept": "application/json",
                                   "User-Agent": "ProductionProgress-release"})
    try:
        with urlopen(request, timeout=30) as response:
            return json.load(response)
    except HTTPError as error:
        # Never print headers, credentials, signed URLs, or arbitrary response bodies.
        raise ValueError(f"Nexus 读取失败：HTTP {error.code}，路径 {path}。") from None
    except (URLError, TimeoutError):
        raise ValueError(f"Nexus 读取失败：网络或超时，路径 {path}。") from None


def release_snapshot(tag, sha256):
    require(tag.startswith("v"), "请使用已存在的 vX.Y.Z 源码标签。")
    version = tag[1:]
    version_tuple(version)
    release = github(f"releases/tags/{quote(tag, safe='')}")
    require(release["draft"] and not release["prerelease"], "必须选择尚未公开的正式 Release 草稿。")
    require(release["tag_name"] == tag, "Release 标签不一致。")
    notes = (release.get("body") or "").replace("\r\n", "\n").strip()
    require(0 < len(notes) <= 65535, "草稿必须包含有效的发布说明。")
    require("English" in notes and "简体中文" in notes, "发布说明应包含 English 和简体中文两个部分。")
    filename = f"ProductionProgress-{version}.zip"
    assets = release["assets"]
    require(len(assets) == 1 and assets[0]["name"] == filename and assets[0]["state"] == "uploaded",
            f"草稿必须只附带已上传完成的 {filename}。")
    require(assets[0]["size"] <= MAX_PACKAGE_SIZE, "安装包超过 100 MiB。")
    reference = github(f"git/ref/tags/{quote(tag, safe='')}")
    require(reference["ref"] == f"refs/tags/{tag}", "找不到准确的源码标签。")
    target = reference["object"]
    for _ in range(8):
        if target["type"] == "commit":
            break
        require(target["type"] == "tag", "标签未指向源码提交。")
        target = github(f"git/tags/{target['sha']}")["object"]
    require(target["type"] == "commit", "标签嵌套过深。")
    commit = target["sha"]
    source = github(f"contents/manifest.json?ref={commit}")
    source_manifest = json.loads(base64.b64decode(source["content"]))
    data = github(f"releases/assets/{assets[0]['id']}", binary=True)
    validate_package(data, version, sha256, source_manifest)
    return {
        "repository": REPOSITORY, "release_id": release["id"], "tag": tag,
        "version": version, "source_commit": commit, "name": release["name"],
        "filename": filename, "asset_id": assets[0]["id"], "sha256": sha256,
        "size": len(data), "notes": notes, "notes_sha256": digest(notes.encode()),
        "nexus_page": NEXUS_PAGE,
    }, data


def output(values):
    if path := os.environ.get("GITHUB_OUTPUT"):
        with open(path, "a", encoding="utf-8") as stream:
            for key, value in values.items():
                # The changelog is passed as a file by the workflow, never as shell code.
                require("\n" not in str(value), "工作流输出必须是单行。")
                stream.write(f"{key}={value}\n")


def summary(text):
    if path := os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(path, "a", encoding="utf-8") as stream:
            stream.write(text + "\n")


def save_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def prepare(args):
    snapshot, data = release_snapshot(args.tag, args.sha256)
    args.directory.mkdir(parents=True, exist_ok=True)
    save_json(args.directory / "release.json", snapshot)
    (args.directory / snapshot["filename"]).write_bytes(data)
    (args.directory / "changelog.txt").write_text(snapshot["notes"], encoding="utf-8")
    output({"version": snapshot["version"], "release_id": snapshot["release_id"]})
    summary(f"## 等待你的发布确认：{snapshot['tag']}\n\n"
            f"- GitHub：{REPOSITORY}（现为草稿）\n- Nexus：{NEXUS_PAGE}\n"
            f"- 文件：`{snapshot['filename']}`，{snapshot['size']} bytes\n"
            f"- SHA-256：`{snapshot['sha256']}`\n- 源码提交：`{snapshot['source_commit']}`\n"
            "- 操作：更新现有 Main / Primary 文件、归档旧文件、同步版本号和以下更新日志；最后公开 GitHub Release。\n"
            "- 请审阅下面的原文和 release-candidate 附件，再由 shy-ci 批准 production-release。\n\n"
            "### 待发布更新日志\n\n" + snapshot["notes"])


def load_candidate(directory):
    snapshot = json.loads((directory / "release.json").read_text(encoding="utf-8"))
    require(snapshot["repository"] == REPOSITORY and snapshot["nexus_page"] == NEXUS_PAGE,
            "发布目标不一致。")
    validate_package((directory / snapshot["filename"]).read_bytes(), snapshot["version"], snapshot["sha256"])
    notes = (directory / "changelog.txt").read_text(encoding="utf-8")
    require(notes == snapshot["notes"] and digest(notes.encode()) == snapshot["notes_sha256"],
            "预览后的更新日志已改变。")
    current, _ = release_snapshot(snapshot["tag"], snapshot["sha256"])
    require(current == snapshot, "等待确认期间 Release、文件或标签已改变；请重新运行并重新确认。")
    return snapshot


def nexus_inventory():
    mod = nexus(NEXUS_MOD)["data"]
    require(str(mod["game_scoped_id"]) == "54196", "Nexus 返回了错误的 mod。")
    mod_id = str(mod["id"])
    files = nexus(f"/mods/{quote(mod_id, safe='')}/files")["data"]["mod_files"]
    candidates = []
    existing = []
    for file in files:
        versions = nexus(f"/mod-files/{quote(str(file['id']), safe='')}/versions")["data"]["versions"]
        existing.extend(versions)
        candidates += [(str(file["id"]), item) for item in versions
                       if item["category"] == "main" and item.get("is_primary")]
    require(len(candidates) == 1, "Nexus 必须有且只有一个 Main / Primary 主文件；需人工核对目标。")
    file_id, previous = candidates[0]
    return mod_id, file_id, previous, existing


def select_nexus_target(snapshot):
    mod_id, file_id, previous, existing = nexus_inventory()
    require(all(item["version"] != snapshot["version"] for item in existing),
            "Nexus 已有同版本文件；可能是上次部分成功。停止重复上传，请核对原运行记录。")
    require(version_tuple(snapshot["version"]) > version_tuple(previous["version"]),
            "只允许发布比当前主文件更新的正式版本。")
    logs = nexus(NEXUS_MOD + "/changelogs.json", legacy=True)
    require(snapshot["version"] not in logs, "Nexus 已有同版本更新日志；停止重复追加，请人工核对。")
    return {"mod_id": mod_id, "file_id": file_id, "previous_version_id": previous["id"],
            "previous_version": previous["version"]}


def connection(args):
    user = nexus("/users/validate.json", legacy=True)
    require(str(user["user_id"]) == "237909389", "API Key 不属于既有 Nexus 发布账户，停止。")
    mod_id, file_id, previous, _ = nexus_inventory()
    details = nexus(NEXUS_MOD + ".json", legacy=True)
    logs = nexus(NEXUS_MOD + "/changelogs.json", legacy=True)
    require(details["version"] == previous["version"], "Nexus 页面与主文件版本不一致。")
    require(previous["version"] in logs, "当前正式版本缺少更新日志。")
    summary("## 只读连接检查通过\n\n"
            f"- Nexus 账户：aaaa2521（237909389）\n- 目标：{NEXUS_PAGE}\n"
            f"- 当前正式版本：{previous['version']}\n- 主文件链：`{file_id}`\n"
            "- 已读取文件、版本号和更新日志；没有上传、追加日志或公开 GitHub Release。")


def check(args):
    snapshot = load_candidate(args.directory)
    target = select_nexus_target(snapshot)
    save_json(args.directory / "target.json", target)
    output({**target, "version": snapshot["version"], "filename": snapshot["filename"]})
    summary(f"确认后复核通过：将 Nexus {target['previous_version']} 更新为 {snapshot['version']}。")


def normalized_lines(text):
    return [line.strip() for line in text.splitlines() if line.strip()]


def verify_nexus(snapshot, target, version_id):
    versions = nexus(f"/mod-files/{quote(target['file_id'], safe='')}/versions")["data"]["versions"]
    published = [item for item in versions if str(item["id"]) == version_id]
    require(len(published) == 1 and published[0]["version"] == snapshot["version"] and
            published[0]["category"] == "main" and published[0].get("is_primary"),
            "Nexus 新文件尚未通过版本、Main 和 Primary 核验。")
    previous = [item for item in versions if item["id"] == target["previous_version_id"]]
    require(len(previous) == 1 and previous[0]["category"] == "archived", "Nexus 旧版本尚未确认归档。")
    details = nexus(NEXUS_MOD + ".json", legacy=True)
    require(details["version"] == snapshot["version"], "Nexus 页面版本尚未更新。")
    logs = nexus(NEXUS_MOD + "/changelogs.json", legacy=True)
    actual = logs.get(snapshot["version"], [])
    require(isinstance(actual, list) and normalized_lines("\n".join(actual)) == normalized_lines(snapshot["notes"]),
            "Nexus 更新日志与确认内容不一致。")


def finish(args):
    snapshot = load_candidate(args.directory)
    target = json.loads((args.directory / "target.json").read_text())
    require(bool(args.version_id), "缺少 Nexus 上传成功返回的版本 ID。")
    # Only reads are retried: Nexus's public metadata can lag behind a successful upload.
    for attempt in range(6):
        try:
            verify_nexus(snapshot, target, args.version_id)
            break
        except ValueError:
            if attempt == 5:
                raise
            time.sleep(10)
    result = github(f"releases/{snapshot['release_id']}", {"draft": False, "make_latest": "true"})
    require(not result["draft"] and result["tag_name"] == snapshot["tag"], "GitHub 公开发布结果不符合预期。")
    summary(f"## 发布完成\n\nGitHub：{result['html_url']}\n\nNexus：{NEXUS_PAGE}\n\n"
            f"文件版本 ID：`{args.version_id}`；版本号和中英文更新日志已回读核验。")


def receipt(args):
    value = {key: os.environ.get(key, "") for key in
             ("GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "CHECK_RESULT", "UPLOAD_RESULT", "FINISH_RESULT", "NEXUS_VERSION_ID")}
    for name in ("release", "target"):
        path = args.directory / f"{name}.json"
        if path.exists():
            value[name] = json.loads(path.read_text(encoding="utf-8"))
    save_json(Path("release-receipt") / "receipt.json", value)
    if value["FINISH_RESULT"] != "success":
        summary("## 未完成双平台发布\n\n请查看 release-receipt 和失败步骤。Nexus 可能已部分成功，"
                "不要直接重复上传或追加日志；先核对文件、版本号、日志及 GitHub 草稿状态。没有自动回滚或删除文件。")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["prepare", "check", "finish", "receipt", "verify-local", "connection"])
    parser.add_argument("--directory", type=Path, default=Path("release-candidate"))
    parser.add_argument("--tag")
    parser.add_argument("--sha256")
    parser.add_argument("--package", type=Path)
    parser.add_argument("--version-id", default=os.environ.get("NEXUS_VERSION_ID", ""))
    args = parser.parse_args()
    try:
        if args.command == "verify-local":
            require(bool(args.package and args.tag and args.sha256), "需提供 --package、--tag 和 --sha256。")
            require(args.tag.startswith("v"), "标签须为 vX.Y.Z。")
            validate_package(args.package.read_bytes(), args.tag[1:], args.sha256)
            print("安装包、版本、SHA-256 与文件白名单检查通过；未调用任何发布接口。")
        else:
            if args.command == "prepare":
                require(bool(args.tag and args.sha256), "需提供 --tag 和 --sha256。")
            globals()[args.command](args)
    except (ValueError, KeyError, OSError, BadZipFile) as error:
        print(f"发布停止：{error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
