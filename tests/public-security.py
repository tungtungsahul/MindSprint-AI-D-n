"""Verify the active HTTPS share without printing or transmitting the backend API key."""
import json
import ipaddress
from pathlib import Path
import socket
import subprocess
import urllib.error
import urllib.request
from urllib.parse import urlsplit

root = Path(__file__).resolve().parent.parent
base = json.loads((root / ".public.selfhost.json").read_text())["url"]
key = (root / ".secrets.selfhost/gemini-api-key").read_text().strip().encode()
assert key, "Gemini secret is not configured"
environment = subprocess.check_output([
    "docker", "inspect", "--format", "{{json .Config.Env}}", "mindsprint-demo-web-1"])
assert key not in environment and b"Gemini__ApiKey=" not in environment
assert b"Gemini__ApiKeyFile=/run/secrets/gemini_api_key" in environment
assert "GEMINI_API_KEY=" not in (root / ".env.selfhost").read_text()
mounts = json.loads(subprocess.check_output([
    "docker", "inspect", "--format", "{{json .Mounts}}", "mindsprint-demo-web-1"]))
assert any(mount["Destination"] == "/run/secrets/gemini_api_key" and not mount["RW"] for mount in mounts)
for name in ("mindsprint-demo-web-1", "mindsprint-demo-db-1", "mindsprint-demo-tunnel-1"):
    ports = json.loads(subprocess.check_output([
        "docker", "inspect", "--format", "{{json .HostConfig.PortBindings}}", name])) or {}
    assert all(binding["HostIp"] == "127.0.0.1" for bindings in ports.values() for binding in bindings)
for path in ("/", "/api.js", "/auth.js", "/runtime-config.js", "/healthz", "/readyz"):
    with urllib.request.urlopen(base + path, timeout=20) as response:
        payload = response.read()
        assert response.status == 200
        assert key not in payload and key not in str(response.headers).encode()
        assert "Kestrel" not in str(response.headers)
        assert response.headers.get("X-Content-Type-Options") == "nosniff"
for path in ("/.env.selfhost", "/.secrets.selfhost/gemini-api-key", "/appsettings.json", "/swagger/index.html"):
    try:
        urllib.request.urlopen(base + path, timeout=15)
        raise AssertionError("Private path is publicly accessible: " + path)
    except urllib.error.HTTPError as error:
        assert error.code in (403, 404), (path, error.code)
with urllib.request.urlopen(base + "/runtime-config.js", timeout=15) as response:
    assert b"window.location.origin" in response.read()
for path in ("/api/ai/flashcards", "/api/ai/quiz"):
    try:
        urllib.request.urlopen(urllib.request.Request(base + path, data=b"{}",
            headers={"Content-Type": "application/json"}), timeout=15)
        raise AssertionError("AI endpoint allows anonymous access")
    except urllib.error.HTTPError as error:
        assert error.code == 401, (path, error.code)
with urllib.request.urlopen("https://api.cloudflare.com/client/v4/ips", timeout=15) as response:
    ranges = json.load(response)
assert ranges["success"]
networks = [ipaddress.ip_network(cidr) for cidr in
            ranges["result"]["ipv4_cidrs"] + ranges["result"]["ipv6_cidrs"]]
addresses = {ipaddress.ip_address(item[4][0]) for item in socket.getaddrinfo(urlsplit(base).hostname, 443)}
assert addresses and all(any(address in network for network in networks) for address in addresses)
print("PASS: HTTPS, private paths, headers, same-origin API, readonly secret, no key in environment/assets, private ports, AI requires login, DNS resolves only to Cloudflare.")
