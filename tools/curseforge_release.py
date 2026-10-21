"""CurseForge's author Upload API; submission is not proof of public approval."""
import json
import os
from urllib.error import HTTPError, URLError
from urllib.request import HTTPRedirectHandler, Request, build_opener
import uuid

API = "https://www.curseforge.com/api"
PROJECT_ID = 1755780
PAGE = "https://www.curseforge.com/stardewvalley/mods/production-progress-crops-and-machines"
GAME_VERSION = "1.6.8"


class UploadError(ValueError):
    """The request can have taken effect if its response was not conclusive."""


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        # Do not forward an author token to a redirect target, or replay a POST.
        return None


def request(path, body=None, content_type=None):
    token = os.environ.get("CURSEFORGE_API_TOKEN", "").strip()
    if not token:
        raise UploadError("production-release 环境未配置 CURSEFORGE_API_TOKEN。")
    headers = {"X-Api-Token": token, "Accept": "application/json", "User-Agent": "ProductionProgress-release"}
    if content_type:
        headers["Content-Type"] = content_type
    method = "POST" if body is not None else "GET"
    req = Request(API + path, data=body, headers=headers, method=method)
    try:
        with build_opener(NoRedirect()).open(req, timeout=90 if body else 30) as response:
            return json.load(response)
    except HTTPError as error:
        raise UploadError(f"CurseForge {method} 失败：HTTP {error.code}，路径 {path}；未自动重试。") from None
    except (URLError, TimeoutError, ValueError):
        raise UploadError(f"CurseForge {method} 网络/响应异常，路径 {path}；需核对服务端结果，未自动重试。") from None


def preflight():
    # This is the game-scoped read endpoint of the author Upload API, not the
    # separate CurseForge for Studios API (which needs a different API key).
    versions = request("/game/stardewvalley/versions")
    if not isinstance(versions, list):
        raise UploadError("CurseForge 游戏版本列表格式与上传接口契约不符。")
    matches = [v for v in versions if v.get("name") == GAME_VERSION]
    if len(matches) != 1 or type(matches[0].get("id")) is not int or matches[0]["id"] <= 0:
        raise UploadError("CurseForge 未返回唯一的 Stardew Valley 1.6.8 版本 ID。")
    return {"project_id": PROJECT_ID, "page": PAGE, "game_version": GAME_VERSION,
            "game_version_id": matches[0]["id"]}


def metadata(snapshot, target):
    if (target.get("project_id") != PROJECT_ID or target.get("page") != PAGE or
            target.get("game_version") != GAME_VERSION or type(target.get("game_version_id")) is not int or
            target["game_version_id"] <= 0):
        raise UploadError("CurseForge 目标项目或游戏版本与预检不一致。")
    return {"displayName": f"Production Progress {snapshot['version']}",
            "changelog": snapshot["notes"], "changelogType": "text",
            "gameVersions": [target["game_version_id"]], "releaseType": "release",
            "isMarkedForManualRelease": False}


def multipart(data, filename, details):
    boundary = "production-progress-" + uuid.uuid4().hex
    chunks = [
        f'--{boundary}\r\nContent-Disposition: form-data; name="metadata"\r\n'
        'Content-Type: application/json; charset=utf-8\r\n\r\n',
        json.dumps(details, ensure_ascii=False),
        f'\r\n--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{filename}"\r\n'
        'Content-Type: application/zip\r\n\r\n',
        data, f"\r\n--{boundary}--\r\n",
    ]
    return b"".join(c.encode("utf-8") if isinstance(c, str) else c for c in chunks), f"multipart/form-data; boundary={boundary}"


def submit(directory, snapshot, validate_package):
    """Called only after approval and Nexus verification; never retry a POST."""
    target = json.loads((directory / "curseforge-target.json").read_text(encoding="utf-8"))
    details = metadata(snapshot, target)
    filename = f"ProductionProgress-{snapshot['version']}.zip"
    if filename != snapshot["filename"]:
        raise UploadError("CurseForge 安装包名称不符合已确认版本。")
    data = (directory / filename).read_bytes()
    validate_package(data, snapshot["version"], snapshot["sha256"])
    receipt_path = directory / "curseforge.json"
    if receipt_path.exists():
        raise UploadError("本次 CurseForge 已有提交记录；先核对服务端结果，不重复上传。")
    receipt = {"project_id": PROJECT_ID, "version": snapshot["version"], "filename": filename,
               "sha256": snapshot["sha256"], "notes_sha256": snapshot["notes_sha256"],
               "game_version": GAME_VERSION, "release_type": "release", "page": PAGE,
               "status": "request-started", "publication_status": "not-verified"}
    def save():
        receipt_path.write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    body, content_type = multipart(data, filename, details)
    save()
    try:
        response = request(f"/projects/{PROJECT_ID}/upload-file", body, content_type)
        if not isinstance(response, dict) or type(response.get("id")) is not int or response["id"] <= 0:
            raise UploadError("CurseForge 上传响应没有有效文件 ID；请先核对后台，禁止盲目重传。")
    except (ValueError, OSError):
        receipt["status"] = "outcome-unknown"
        save()
        raise
    receipt.update(status="submitted", file_id=response["id"])
    save()
    return receipt


def verify_receipt(receipt, snapshot):
    if (receipt.get("status") != "submitted" or receipt.get("project_id") != PROJECT_ID or
            receipt.get("sha256") != snapshot["sha256"] or receipt.get("notes_sha256") != snapshot["notes_sha256"] or
            receipt.get("version") != snapshot["version"] or type(receipt.get("file_id")) is not int or
            receipt["file_id"] <= 0):
        raise UploadError("CurseForge 提交回执与本次确认内容不一致。")
