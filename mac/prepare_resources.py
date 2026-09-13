from pathlib import Path
import base64
import hashlib
import re
import sys

root = Path(sys.argv[1])
html = (root / 'app.html').read_text(encoding='utf-8')
scripts = [(root / 'vendor' / name).read_text(encoding='utf-8') for name in ['purify.js', 'marked.js']]
for script in scripts:
    assert '</script' not in script.lower(), 'Unexpected script terminator in bundled dependency'
html = html.replace('<script>', ''.join('<script>' + text + '\n</script>' for text in scripts) + '<script>', 1)
hashes = ["'sha256-" + base64.b64encode(hashlib.sha256(text.encode('utf-8')).digest()).decode('ascii') + "'" for text in re.findall(r'<script>(.*?)</script>', html, flags=re.S)]
assert len(hashes) == 3
html = re.sub(r"script-src [^;]+;", 'script-src ' + ' '.join(hashes) + ';', html, count=1)
assert 'SCRIPT_HASH' not in html
(root / 'app.html').write_text(html, encoding='utf-8', newline='\n')
