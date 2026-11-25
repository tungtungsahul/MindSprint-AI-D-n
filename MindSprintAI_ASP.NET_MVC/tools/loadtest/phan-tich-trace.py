#!/usr/bin/env python3
# Đọc file speedscope của dotnet-trace (tools/debian/trace-cpu.sh) từng đoạn, không nạp cả file (file ~300 MB).
# Chạy TRÊN Debian (đủ RAM, có python3):  nice -n 19 python3 phan-tich-trace.py ~/mindsprint-cpu.speedscope.json
# In: % CPU (mẫu đang chạy, bỏ hàm chờ) theo thao tác và theo hàm ứng dụng sâu nhất. Lần đầu dùng 14/11/2026.
import json, re, sys, collections
path = sys.argv[1]
WAIT = re.compile(r'UNMANAGED_CODE_TIME|Wait|Sleep|epoll|LowLevelLifoSemaphore|Monitor\.|SemaphoreSlim|PortableThreadPool.*Worker|ReadAsync|Receive|Socket|Blocking')
ACT = re.compile(r'Controllers\.(\w+)Controller\+<(\w+)>|Services\.(\w+Middleware)\+<InvokeAsync>|(SignalR)')
APP = re.compile(r'MindSprintAI_ASP.NET_MVC!MindSprintAI_ASP.NET_MVC\.(?:Services|Data)\.([\w.+]+?)\+<(\w+)>')
def act_name(n):
    m = ACT.search(n)
    if not m: return None
    return m.group(1) + '.' + m.group(2) if m.group(1) else (m.group(3) or m.group(4))
def app_name(n):
    m = APP.search(n)
    return m.group(1) + '.' + m.group(2) if m else None
with open(path, 'r', encoding='utf-8') as f:
    head = f.read(2_000_000)
    i = head.index('"profiles"'); fs = head.index('"frames"')
    frames = [x['name'] for x in json.loads(head[head.index('[', fs):head.rindex(']', 0, i) + 1])]
    iswait = [bool(WAIT.search(n)) for n in frames]
    act_of = [act_name(n) for n in frames]; app_of = [app_name(n) for n in frames]
    ev = re.compile(r'"name": "([^"]*)", "unit"|\{ "type": "([OC])", "frame": (\d+), "at": ([0-9.]+) \}')
    by_act = collections.Counter(); by_pair = collections.Counter(); busy = 0.0
    stack = []; last = None
    buf = head[i:]
    while True:
        pos = 0
        for m in ev.finditer(buf):
            pos = m.end()
            if m.group(1) is not None:
                stack.clear(); last = None; continue
            t = float(m.group(4))
            if last is not None and stack and t > last and not iswait[stack[-1]]:
                d = t - last; busy += d
                act = next((act_of[x] for x in stack if act_of[x]), '(khác)')
                inner = next((app_of[x] for x in reversed(stack) if app_of[x]), '-')
                by_act[act] += d; by_pair[(act, inner)] += d
            last = t
            if m.group(2) == 'O': stack.append(int(m.group(3)))
            elif stack: stack.pop()
        chunk = f.read(8_000_000)
        if not chunk: break
        buf = buf[pos:] + chunk
print(f"Thời gian đang chạy: {busy/1000:.0f} s")
print("\n== CPU theo thao tác (controller/middleware)")
for k, v in by_act.most_common(25): print(f"{v/busy*100:6.2f}%  {k}")
print("\n== CPU theo (thao tác, hàm ứng dụng sâu nhất)")
for k, v in by_pair.most_common(45): print(f"{v/busy*100:6.2f}%  {k[0]}  ->  {k[1]}")
