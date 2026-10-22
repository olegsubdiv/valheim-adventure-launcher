import base64, contextlib, hashlib, importlib.util, io, json, pathlib, sys, tempfile, unittest, zipfile

ROOT=pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'publisher'))
from build_texture_variant import build

class TexturePublisherTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='arpg-texture-publisher-')
        self.root=pathlib.Path(self.temp.name)
        self.archive=self.root/'Medium.7z'
        self.baseline=self.root/'baseline.json'
        payload=dict(artifacts=[dict(id='Willybach-HDValheim_Higher-2.0.0',sha256='a'*64)],files=[])
        self.baseline.write_text(json.dumps(dict(payload=base64.b64encode(json.dumps(payload).encode()).decode())))
    def tearDown(self): self.temp.cleanup()
    def archive_files(self,extra=()):
        with zipfile.ZipFile(self.archive,'w') as archive:
            archive.writestr('HDValheimTextures.dll',b'loader')
            archive.writestr('HDValheimTexturesData/Textures.dat',b'medium-textures')
            for name,value in extra: archive.writestr(name,value)
    def run_build(self):
        with contextlib.redirect_stdout(io.StringIO()):build(self.archive,self.baseline,self.root/'out')
    def test_texture_inventory_excludes_loading_screens_and_gameplay(self):
        self.archive_files([('CustomLoadingScreens/a.png',b'not installed'),('BepInEx/config/gameplay.cfg',b'not installed')])
        self.run_build()
        payload=json.loads((self.root/'out/manifest.payload.json').read_text())
        self.assertEqual(len(payload['files']),2)
        self.assertEqual(payload['textureVariant']['baseArtifactSha256'],'a'*64)
        self.assertEqual(payload['artifacts'][0]['urls'],[])
        textures=next(row for row in payload['files'] if row['path'].endswith('Textures.dat'))
        self.assertEqual(textures['sha256'],hashlib.sha256(b'medium-textures').hexdigest())
        self.assertFalse((self.root/'out/extracted/BepInEx/config/gameplay.cfg').exists())
    def test_case_collision_aborts_before_extracting(self):
        self.archive_files([('hdvalheimtextures.dll',b'collision')])
        with self.assertRaises(ValueError):self.run_build()
        self.assertFalse((self.root/'out/extracted').exists())
    def test_traversal_aborts_before_extracting(self):
        self.archive_files([('../outside.txt',b'bad')])
        with self.assertRaises(ValueError):self.run_build()
        self.assertFalse((self.root/'outside.txt').exists())
    def test_links_abort_before_extracting(self):
        self.archive_files()
        with zipfile.ZipFile(self.archive,'a') as archive:
            entry=zipfile.ZipInfo('HDValheimTexturesData/linked');entry.create_system=3;entry.external_attr=0o120777<<16
            archive.writestr(entry,'../../outside.txt')
        with self.assertRaises(ValueError):self.run_build()
        self.assertFalse((self.root/'out/extracted').exists())
    def test_existing_extraction_is_not_overwritten(self):
        self.archive_files();self.run_build()
        with self.assertRaises(FileExistsError):self.run_build()

if __name__=='__main__':unittest.main()
