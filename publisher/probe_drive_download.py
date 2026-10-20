#!/usr/bin/env python3
"""Bounded anonymous Google Drive download probe; no Google account or tokens."""
import argparse
import http.cookiejar
import json
import re
import urllib.parse
import urllib.request
from html.parser import HTMLParser

class DownloadForm(HTMLParser):
    def __init__(self):
        super().__init__()
        self.action = None
        self.fields = {}
        self.in_form = False
    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "form" and attrs.get("id") == "download-form":
            self.action = attrs.get("action")
            self.in_form = True
        if self.in_form and tag == "input" and attrs.get("type") == "hidden" and attrs.get("name") in ("id", "export", "confirm", "uuid", "at"):
            self.fields[attrs["name"]] = attrs.get("value", "")
    def handle_endtag(self, tag):
        if tag == "form":
            self.in_form = False

class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        parsed = urllib.parse.urlparse(newurl)
        host = parsed.hostname or ""
        if parsed.scheme != "https" or not (host in ("drive.google.com", "drive.usercontent.google.com") or host.endswith(".googleusercontent.com")):
            raise ValueError("Unexpected public-download redirect host")
        return super().redirect_request(req, fp, code, msg, headers, newurl)

def probe(file_id: str, expected_size: int | None = None) -> dict:
    if not re.fullmatch(r"[A-Za-z0-9_-]{10,120}", file_id):
        raise ValueError("Invalid Drive file ID")
    opener = urllib.request.build_opener(SafeRedirect(), urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    url = "https://drive.google.com/uc?export=download&id=" + file_id
    confirmation = False
    for _ in range(3):
        req = urllib.request.Request(url, headers={"Range": "bytes=0-1023", "Accept-Encoding": "identity", "User-Agent": "ValheimAdventureLauncher/0.1"})
        with opener.open(req, timeout=60) as response:
            content_type = response.headers.get("Content-Type", "")
            raw = response.read(65536 if "text/html" in content_type else 1024)
            if "text/html" not in content_type:
                content_range = response.headers.get("Content-Range", "")
                match = re.fullmatch(r"bytes 0-1023/(\d+)", content_range)
                size = int(match.group(1)) if match else None
                if response.status != 206 or not match or len(raw) != 1024:
                    raise ValueError("Server did not honor the bounded byte range")
                if expected_size is not None and size != expected_size:
                    raise ValueError("Public file size differs from expected size")
                return {"anonymous": True, "status": response.status, "host": urllib.parse.urlparse(response.url).hostname,
                        "contentType": content_type, "contentRange": content_range, "bytes": size,
                        "sampleBytes": len(raw), "magic": raw[:4].hex(), "confirmationHandled": confirmation}
            form = DownloadForm()
            form.feed(raw.decode("utf-8", errors="replace"))
            if not form.action or form.fields.get("id") != file_id:
                raise ValueError("No supported public-download confirmation form")
            action = urllib.parse.urljoin(response.url, form.action)
            parsed = urllib.parse.urlparse(action)
            if parsed.scheme != "https" or parsed.hostname not in ("drive.google.com", "drive.usercontent.google.com"):
                raise ValueError("Unexpected download confirmation host")
            url = action + "?" + urllib.parse.urlencode(form.fields)
            confirmation = True
    raise ValueError("Download confirmation did not finish")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--file-id", required=True)
    parser.add_argument("--expected-size", type=int)
    args = parser.parse_args()
    print(json.dumps(probe(args.file_id, args.expected_size), indent=2))
