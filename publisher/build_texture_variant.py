#!/usr/bin/env python3
"""Inventory the official Medium2.0 archive without changing installed profiles."""
from __future__ import annotations
import argparse, datetime as dt, json, pathlib, subprocess
from build_manifest import digest_file, read_json, safe_path, reject_duplicates

HIGHER_ID='Willybach-HDValheim_Higher-2.0.0'
MEDIUM_ID='Willybach-HDValheim_Medium-2.0.0'

def build(archive:pathlib.Path, baseline:pathlib.Path, output:pathlib.Path, version:str='2.0.0'):
    if archive.is_symlink() or not archive.is_file(): raise ValueError('Medium archive is missing or linked')
    envelope=read_json(baseline)
    import base64
    original=json.loads(base64.b64decode(envelope['payload']))
    higher=next(a for a in original['artifacts'] if a['id']==HIGHER_ID)
    sha=digest_file(archive)
    archive_list=subprocess.run(['tar','-tf',str(archive)],capture_output=True,text=True,encoding='utf-8',errors='strict',check=True).stdout.splitlines()
    entries=[]
    for raw in archive_list:
        path=safe_path(raw.rstrip('/'))
        if raw.endswith('/'): continue
        entries.append(path)
    reject_duplicates(entries,'Medium archive')
    verbose=subprocess.run(['tar','-tvf',str(archive)],capture_output=True,text=True,encoding='utf-8',errors='strict',check=True).stdout.splitlines()
    if any(line and line[0] not in '-d' for line in verbose):
        raise ValueError('Archive contains links or special filesystem entries')
    selected=[p for p in entries if p=='HDValheimTextures.dll' or p.startswith('HDValheimTexturesData/')]
    if 'HDValheimTextures.dll' not in selected or 'HDValheimTexturesData/Textures.dat' not in selected:
        raise ValueError('Archive is not the expected complete HD plugin')
    # Do not overwrite any previous extraction. All sources remain outside live profiles.
    output.mkdir(parents=True,exist_ok=True)
    extracted=output/'extracted'
    extracted.mkdir(exist_ok=False)
    subprocess.run(['tar','-xf',str(archive),'-C',str(extracted),'--',*selected],check=True)
    files=[]
    for entry in selected:
        source=extracted/pathlib.Path(entry)
        for ancestor in [source,*source.parents]:
            if ancestor==extracted: break
            if ancestor.is_symlink() or (hasattr(ancestor,'is_junction') and ancestor.is_junction()):
                raise ValueError('Archive links are not permitted')
        if not source.is_file(): raise ValueError('Extracted archive member missing: '+entry)
        files.append(dict(path='BepInEx/plugins/'+entry,artifactId=MEDIUM_ID,entry=entry,size=source.stat().st_size,sha256=digest_file(source),policy='managed',preserveKeys=[]))
    artifact=dict(id=MEDIUM_ID,kind='7z',fileName=archive.name,size=archive.stat().st_size,sha256=sha,urls=[],manualUrl='https://www.nexusmods.com/valheim/mods/1030?tab=files&file_id=20493',instructions='Choose the official HD Texture Pack MEDIUM2.0 archive, or enter the company access code for automatic download.')
    manifest=dict(schemaVersion=1,packId='adventure-rpg-textures-medium',title='Willybach HD Medium textures',version=version,publishedAt=dt.datetime.now(dt.timezone.utc).isoformat(),artifacts=[artifact],files=files,textureVariant=dict(id='medium',baseArtifactId=HIGHER_ID,baseArtifactSha256=higher['sha256']))
    (output/'manifest.payload.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
    baseline_hd={f['path']:f for f in original['files'] if f['artifactId']==HIGHER_ID}
    medium_paths={f['path'] for f in files}
    report=dict(version=version,archiveBytes=artifact['size'],archiveSha256=sha,files=len(files),installedBytes=sum(f['size'] for f in files),unchangedFromHigher=sum(baseline_hd.get(f['path'],{}).get('sha256')==f['sha256'] for f in files),higherOnlyPaths=sorted(set(baseline_hd)-medium_paths),mediumOnlyPaths=sorted(medium_paths-set(baseline_hd)),liveProfileChanged=False,gameLaunched=False)
    (output/'receipt.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report,indent=2))

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--archive',type=pathlib.Path,required=True)
    p.add_argument('--baseline',type=pathlib.Path,required=True,help='Previously signature-verified original collection manifest')
    p.add_argument('--output',type=pathlib.Path,required=True)
    p.add_argument('--version',default='2.0.0')
    a=p.parse_args();build(a.archive,a.baseline,a.output,a.version)
