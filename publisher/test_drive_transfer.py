#!/usr/bin/env python3
"""Create a large synthetic ZIP64 for a private transport check, never game content."""
import argparse
import hashlib
import json
import pathlib
import shutil
import zipfile

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--output", type=pathlib.Path, required=True)
    p.add_argument("--bytes", type=int, default=6_900_000_000)
    args=p.parse_args()
    if args.bytes < 1:
        p.error("--bytes must be positive")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    if args.output.exists():
        raise SystemExit("Destination already exists; choose a fresh test filename.")
    if shutil.disk_usage(args.output.parent).free < args.bytes + 1024**3:
        raise SystemExit("Not enough free space for the synthetic test.")
    chunk=bytes(range(256))*4096
    info=zipfile.ZipInfo("synthetic-transport-test.bin", date_time=(2026,1,1,0,0,0))
    info.compress_type=zipfile.ZIP_STORED
    with zipfile.ZipFile(args.output,"w",allowZip64=True) as z:
        with z.open(info,"w",force_zip64=True) as f:
            remaining=args.bytes
            while remaining:
                block=chunk[:min(remaining,len(chunk))]
                f.write(block)
                remaining-=len(block)
    sha=hashlib.sha256()
    with args.output.open("rb") as f:
        for chunk in iter(lambda:f.read(1024*1024),b""):
            sha.update(chunk)
    print(json.dumps({"file":str(args.output),"bytes":args.output.stat().st_size,"sha256":sha.hexdigest(),"content":"synthetic; no mods or personal data"}))
if __name__=="__main__":
    main()
