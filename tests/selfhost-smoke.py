"""Check localhost; --recovery interrupts containers, --ai calls Gemini with synthetic text."""
import argparse
import json
from pathlib import Path
import secrets
import subprocess
import time
import urllib.error
import urllib.request

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("base", nargs="?", default="http://localhost:8080")
parser.add_argument("--recovery", action="store_true")
parser.add_argument("--ai", action="store_true")
args = parser.parse_args()
base = args.base.rstrip("/")
if args.recovery and base not in ("http://localhost:8080", "http://127.0.0.1:8080"):
    parser.error("--recovery only operates on the local Docker stack")
token = None


def request(path, method="GET", body=None, expected=200, authenticated=True, origin=None):
    headers = {"Content-Type": "application/json", "User-Agent": "MindSprint-Selfhost-Check/1.0"}
    if token and authenticated:
        headers["Authorization"] = "Bearer " + token
    if origin:
        headers["Origin"] = origin
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(base + path, data=data, headers=headers, method=method)
    try:
        response = urllib.request.urlopen(req, timeout=120 if path.startswith("/api/ai/") else 10)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        assert response.status == expected, (path, response.status, expected)
        return response.read().decode()


def compose(*command):
    return subprocess.run(["docker", "compose", "--env-file", ".env.selfhost", "-f",
                           "compose.selfhost.yaml", *command],
                          cwd=Path(__file__).resolve().parent.parent, check=True,
                          capture_output=True, text=True).stdout


def wait_ready():
    deadline = time.monotonic() + 180
    while time.monotonic() < deadline:
        try:
            if json.loads(request("/readyz"))["status"] == "ready":
                return
        except (AssertionError, urllib.error.URLError, TimeoutError, ConnectionError):
            pass
        time.sleep(1)
    raise AssertionError("Web/database did not recover within 180 seconds")


assert json.loads(request("/healthz"))["status"] == "ok"
assert json.loads(request("/readyz"))["status"] == "ready"
assert "MindSprint" in request("/")
assert "window.location.origin" in request("/runtime-config.js")
for path in ("/.env.selfhost", "/appsettings.json", "/swagger/index.html", "/js/auth.js"):
    request(path, expected=404)
request("/api/auth/demo", "POST", {}, expected=404)
request("/api/tasks", expected=401)
google = json.loads(request("/api/auth/google/config"))
if google["enabled"]:
    challenge = json.loads(request("/api/auth/google/challenge", origin="http://localhost:8080"))
    assert len(challenge["nonce"]) == 64
    request("/api/auth/google/challenge", origin="https://untrusted.example", expected=403)

credentials = {"email": "hosting-check-" + secrets.token_hex(8) + "@example.com",
               "password": secrets.token_urlsafe(24), "displayName": "Hosting check"}
account = json.loads(request("/api/auth/register", "POST", credentials))
login = json.loads(request("/api/auth/login", "POST", credentials))
assert account["user"]["id"] == login["user"]["id"]
token = login["token"]
task = json.loads(request("/api/tasks", "POST", {"title": "Self-hosting smoke check"}))
card = notebook = None
try:
    assert any(item["id"] == task["id"] for item in json.loads(request("/api/tasks")))
    card = json.loads(request("/api/flashcards", "POST", {
        "category": "selfhost-check", "question": "Cơ sở dữ liệu?", "answer": "SQL Server"}))
    assert any(item["id"] == card["id"] and item["question"] == "Cơ sở dữ liệu?"
               for item in json.loads(request("/api/flashcards")))
    notebook = json.loads(request("/api/notebooks", "POST", {"title": "Kiểm tra self-host"}))
    request(f"/api/notebooks/{notebook['id']}/sources/text", "POST",
            {"title": "Nguồn thử", "text": "SQL Server lưu dữ liệu học tập bằng tiếng Việt."})
    assert len(json.loads(request(f"/api/notebooks/{notebook['id']}/sources"))) == 1
    if args.ai:
        cards = json.loads(request("/api/ai/flashcards", "POST", {
            "text": "Trái Đất quay quanh Mặt Trời. Một năm có mười hai tháng.", "count": 1}))
        assert cards and cards[0]["question"] and cards[0]["answer"]
        print("PASS: Gemini returned flashcards for synthetic study text.")
    if args.recovery:
        before = json.loads(compose("ps", "--format", "json", "web"))
        try:
            compose("stop", "db")
            assert json.loads(request("/healthz"))["status"] == "ok"
            started = time.monotonic()
            assert json.loads(request("/readyz", expected=503))["status"] == "unavailable"
            assert time.monotonic() - started < 8, "Readiness must fail promptly"
        finally:
            compose("start", "db")
            wait_ready()
        # PID 1 accepts SIGTERM because .NET registers a handler. SIGKILL sent by
        # another process in the same PID namespace is ignored by the kernel.
        container_id = before["ID"]
        def restart_count():
            return int(subprocess.check_output([
                "docker", "inspect", "--format", "{{.RestartCount}}", container_id], text=True))
        previous_restarts = restart_count()
        compose("exec", "-T", "web", "sh", "-c", "kill -TERM 1")
        deadline = time.monotonic() + 60
        while restart_count() <= previous_restarts and time.monotonic() < deadline:
            time.sleep(1)
        assert restart_count() > previous_restarts, "Docker must restart the terminated app process"
        wait_ready()
        assert json.loads(compose("ps", "--format", "json", "web"))["ID"] == container_id
        assert any(item["id"] == task["id"] for item in json.loads(request("/api/tasks")))
        assert any(item["id"] == card["id"] for item in json.loads(request("/api/flashcards")))
        assert len(json.loads(request(f"/api/notebooks/{notebook['id']}/sources"))) == 1
        print("PASS: readiness detects database outage; database and web recover with data retained.")
finally:
    if notebook:
        request("/api/notebooks/" + str(notebook["id"]), "DELETE", expected=204)
    if card:
        request("/api/flashcards/" + card["id"], "DELETE", expected=204)
    request("/api/tasks/" + str(task["id"]), "DELETE", expected=204)
    request("/api/auth/revoke-all", "POST", {})
print("PASS: frontend, readiness, production auth, login, tasks, cards, notebook sources and Google config/origin checks.")
print("One isolated hosting-check account was created in the demo database; its sessions were revoked.")
