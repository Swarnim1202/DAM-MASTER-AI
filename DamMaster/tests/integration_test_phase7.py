"""Phase 7 end-to-end checks: offline field sync with photos, OCR, single sign-on (OIDC), federation of instances.
Run after Phase 1–6 tests. Main API started with Ocr__Enabled=true, Sso__Enabled=true, Sso__Authority=http://127.0.0.1:5098,
Sso__ClientId=dammaster, Sso__ClientSecret=secret, Federation__Instances__0 = second instance (DM_API2) with key DM_FED_KEY."""
import os, sys, io, json, time, uuid, base64, hashlib, threading, urllib.parse, requests
from http.server import BaseHTTPRequestHandler, HTTPServer
from PIL import Image, ImageDraw, ImageFont
from cryptography.hazmat.primitives.asymmetric import rsa, padding
from cryptography.hazmat.primitives import hashes
B=os.environ.get('DM_API','http://localhost:5080')+'/api'; B2=os.environ.get('DM_API2','http://localhost:5081')+'/api'; H={'X-Requested-With':'DamMaster'}
def S(): s=requests.Session(); s.headers.update(H); return s
def login(u,p,base=B):
  s=S(); r=s.post(base+'/auth/login',json={'username':u,'password':p}); assert r.status_code==200,(u,r.text); return s
def ok(c,m):
  print(('PASS ' if c else 'FAIL ')+m)
  if not c: sys.exit(1)
dse=login('arjun','Temp@2026'); ins=login('meera','Temp@2026'); mg=login('director','Temp@2026'); vik=login('vikram','Temp@2026')
D=[d for d in dse.get(B+'/dams').json() if d['code']=='DM-001'][0]['id']
# ---------------- offline field inspection ----------------
I=ins.post(B+'/inspections',json={'damId':D,'inspectionType':'Routine','scheduledDate':'2026-10-12','inspector':'Meera Nair'}).json()['id']
pk=ins.get(B+f'/field/package/{I}').json(); items=pk['checklist']
ok(len(items)==12 and len(pk['sensors'])>=8 and pk['inspection']['status']=='Planned','offline package: inspection, 12-item checklist and sensors downloaded')
run=uuid.uuid4().hex[:6]; fid=f'op-{run}-3'
ops=[{'clientOpId':f'op-{run}-1','kind':'start','inspectionId':I,'payload':{}},
     {'clientOpId':f'op-{run}-2','kind':'checks','inspectionId':I,'payload':[{'itemCode':x['code'],'result':'Attention' if x['code']=='CON-02' else 'OK','remark':'Fresh calcite at joint 14/15' if x['code']=='CON-02' else None} for x in items]},
     {'clientOpId':fid,'kind':'finding','inspectionId':I,'payload':{'component':'Galleries','location':'Joint 14/15','description':'Fresh calcite and damp patch at lift joint','severity':'High','recommendation':'Monitor drain D-15 weekly'}},
     {'clientOpId':f'op-{run}-4','kind':'complete','inspectionId':I,'payload':{'overallCondition':'Fair','summary':'One high finding in gallery'}}]
ok(vik.post(B+'/field/sync',json={'ops':ops}).status_code==403,'instrumentation engineer cannot sync inspections')
r=ins.post(B+'/field/sync',json={'ops':ops}).json()
ok(all(x['ok'] and not x['replayed'] for x in r),'4 queued operations applied in order (start, checks, finding, complete)')
fid_server=r[2]['entityId']; ok(fid_server is not None,'finding created and mapped to the device id')
r2=ins.post(B+'/field/sync',json={'ops':ops}).json()
ok(all(x['replayed'] for x in r2) and r2[2]['entityId']==fid_server,'replaying the same queue (lost response) changes nothing')
det=ins.get(B+f'/inspections/{I}').json(); ok(det['inspection']['status']=='Completed' and len(det['findings'])==1,'server state: completed, exactly one finding')
ok(any(a['ruleType']=='Inspection' and 'calcite' in a['title'] for a in dse.get(B+f'/alerts?damId={D}').json()),'High finding from the field raised a Level 2 alert')
# two devices / two tabs sending the same queue at the same moment
I3=ins.post(B+'/inspections',json={'damId':D,'inspectionType':'Special','scheduledDate':'2026-10-12'}).json()['id']
cops=[{'clientOpId':f'cc-{run}-1','kind':'start','inspectionId':I3,'payload':{}},{'clientOpId':f'cc-{run}-2','kind':'finding','inspectionId':I3,'payload':{'component':'Crest & parapet','description':'Parapet crack','severity':'Medium'}}]
res=[None,None]
def go(k): res[k]=ins.post(B+'/field/sync',json={'ops':cops}).json()
th=[threading.Thread(target=go,args=(k,)) for k in (0,1)]; [t.start() for t in th]; [t.join() for t in th]
ok(len(ins.get(B+f'/inspections/{I3}').json()['findings'])==1 and res[0][1]['entityId']==res[1][1]['entityId'],'two simultaneous syncs of the same queue → one finding (atomic claim of the operation id)')
I2=ins.post(B+'/inspections',json={'damId':D,'inspectionType':'Routine','scheduledDate':'2026-10-12'}).json()['id']
bad=ins.post(B+'/field/sync',json={'ops':[{'clientOpId':f'op-{run}-9','kind':'complete','inspectionId':I2,'payload':{'overallCondition':'Good'}}]}).json()
ok(not bad[0]['ok'] and 'not yet recorded' in bad[0]['error'] or 'in progress' in bad[0]['error'],f"invalid operation rejected with the web page's rule: {bad[0]['error']}")
# photos
def jpeg(w=1600,h=1200):
  im=Image.new('RGB',(w,h),(120,130,140)); ImageDraw.Draw(im).rectangle([200,200,900,700],fill=(200,190,170)); b=io.BytesIO(); im.save(b,'JPEG',quality=80); return b.getvalue()
ph=ins.post(B+f'/findings/{fid}/photos',files={'file':('IMG_001.jpg',jpeg(),'image/jpeg')},data={'latitude':'19.0761','longitude':'73.8569','takenAt':'2026-10-12T10:15:00+05:30','caption':'Calcite at joint 14/15','clientRef':f'ph-{run}-1'}).json()
ok(ph['findingId']==fid_server and ph['width']==1600 and ph['height']==1200 and float(ph['latitude'])==19.0761,'photo attached via the offline finding id, size 1600×1200 and GPS read')
ph2=ins.post(B+f'/findings/{fid}/photos',files={'file':('IMG_001.jpg',jpeg(),'image/jpeg')},data={'clientRef':f'ph-{run}-1'}).json(); ok(ph2['id']==ph['id'],'re-uploading the same photo (same client ref) does not duplicate it')
b=io.BytesIO(); Image.new('RGB',(640,480),(0,90,0)).save(b,'PNG'); ok(ins.post(B+f'/findings/{fid_server}/photos',files={'file':('p.png',b.getvalue(),'image/png')}).json()['width']==640,'PNG accepted')
ok('JPEG or PNG' in ins.post(B+f'/findings/{fid_server}/photos',files={'file':('x.jpg',b'not an image','image/jpeg')}).json()['error'],'non-image content refused (checked by file signature)')
ok(len(dse.get(B+f'/findings/{fid_server}/photos').json())==2 and dse.get(B+f"/photos/{ph['id']}").content[:2]==b'\xff\xd8','photos listed and downloadable')
# ---------------- OCR ----------------
img=Image.new('L',(1700,900),255); dr=ImageDraw.Draw(img)
try: font=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',44)
except Exception: font=ImageFont.load_default()
for k,line in enumerate(['INSPECTION NOTE 14 MARCH 2025','SPILLWAY GATE 3 HOIST ROPE CORRODED','REPLACE ROPE BEFORE MONSOON','DRAIN D-15 FLOW REDUCED']): dr.text((80,120+k*150),line,fill=0,font=font)
pdf=io.BytesIO(); img.save(pdf,'PDF',resolution=200)
doc=dse.post(B+f'/dams/{D}/documents',json={'docType':'Inspection report','title':'Scanned inspection note 2025','status':'Approved'}).json()
up=dse.post(B+f"/dams/{D}/documents/{doc['id']}/files",files={'file':('scan.pdf',pdf.getvalue(),'application/pdf')}).json()
ok(up['extractedOk'] and up['ocrUsed'] and up['textChars']>50,f"scanned PDF read by OCR ({up['textChars']} characters)")
h=dse.get(B+'/library/search',params={'q':'hoist rope corroded'}).json(); ok(any(x['title']=='Scanned inspection note 2025' for x in h),'OCR text is searchable: "hoist rope corroded" found')
# ---------------- SSO (OpenID Connect) ----------------
key=rsa.generate_private_key(public_exponent=65537,key_size=2048); pub=key.public_key().public_numbers()
b64=lambda b: base64.urlsafe_b64encode(b).rstrip(b'=').decode()
i2b=lambda n: n.to_bytes((n.bit_length()+7)//8,'big')
ISS='http://127.0.0.1:5098'; state={'mode':'ok','req':{}}
def idtok(claims,bad=False):
  h=b64(json.dumps({'alg':'RS256','kid':'k1','typ':'JWT'}).encode()); p=b64(json.dumps(claims).encode())
  sig=key.sign(f'{h}.{p}'.encode(),padding.PKCS1v15(),hashes.SHA256())
  if bad: sig=bytes([sig[0]^1])+sig[1:]
  return f'{h}.{p}.{b64(sig)}'
class OIDC(BaseHTTPRequestHandler):
  def j(self,o,code=200):
    out=json.dumps(o).encode(); self.send_response(code); self.send_header('Content-Type','application/json'); self.send_header('Content-Length',str(len(out))); self.end_headers(); self.wfile.write(out)
  def do_GET(self):
    u=urllib.parse.urlparse(self.path); q=dict(urllib.parse.parse_qsl(u.query))
    if u.path=='/.well-known/openid-configuration': return self.j({'issuer':ISS,'authorization_endpoint':ISS+'/authorize','token_endpoint':ISS+'/token','jwks_uri':ISS+'/jwks'})
    if u.path=='/jwks': return self.j({'keys':[{'kty':'RSA','kid':'k1','use':'sig','alg':'RS256','n':b64(i2b(pub.n)),'e':b64(i2b(pub.e))}]})
    if u.path=='/authorize':
      state['req']=q; self.send_response(302); self.send_header('Location',q['redirect_uri']+'?'+urllib.parse.urlencode({'code':'c0de','state':q['state']})); self.end_headers(); return
    self.j({},404)
  def do_POST(self):
    form=dict(urllib.parse.parse_qsl(self.rfile.read(int(self.headers['Content-Length'])).decode())); rq=state['req']
    if form.get('code')!='c0de' or b64(hashlib.sha256(form['code_verifier'].encode()).digest())!=rq['code_challenge'] or form.get('client_secret')!='secret': return self.j({'error':'invalid_grant'},400)
    m=state['mode']; now=int(time.time())
    claims={'iss':ISS,'aud':'dammaster','sub':'kc-1234','email':'unknown@example.org' if m=='unknown' else 'arjun@example.org','iat':now,'exp':now+300,'nonce':'wrong' if m=='nonce' else rq['nonce']}
    self.j({'access_token':'x','token_type':'Bearer','id_token':idtok(claims,bad=(m=='badsig'))})
  def log_message(self,*a): pass
srv=HTTPServer(('127.0.0.1',5098),OIDC); threading.Thread(target=srv.serve_forever,daemon=True).start()
ok(requests.get(B+'/auth/sso/config').json()['enabled'],'SSO enabled; login page offers the organisation account')
def sso(mode):
  state['mode']=mode; s=S(); r=s.get(B+'/auth/sso/login',params={'returnUrl':'/alerts'},allow_redirects=False); loc=r.headers['Location']
  rq=dict(urllib.parse.parse_qsl(urllib.parse.urlparse(loc).query)); assert rq['code_challenge_method']=='S256' and rq['nonce'] and rq['state']
  r=s.get(loc,allow_redirects=False); r=s.get(r.headers['Location'],allow_redirects=False); return s, r.headers['Location']
s,loc=sso('ok'); me=s.get(B+'/auth/me')
ok(loc=='/alerts' and me.status_code==200 and me.json()['username']=='arjun','SSO sign-in (code + PKCE, RS256 id_token) → signed in as arjun, returned to /alerts')
_,loc=sso('badsig'); ok('signature' in urllib.parse.unquote(loc),'tampered id_token signature rejected')
_,loc=sso('nonce'); ok('nonce' in urllib.parse.unquote(loc),'replayed / wrong nonce rejected')
_,loc=sso('unknown'); ok('No DAM MASTER account' in urllib.parse.unquote(loc),'identity without a DAM MASTER account refused (no auto-creation)')
r=S().get(B+'/auth/sso/callback',params={'code':'c0de','state':'forged'},allow_redirects=False); ok('expired' in urllib.parse.unquote(r.headers['Location']) or 'state' in urllib.parse.unquote(r.headers['Location']),'callback without a matching sign-in session refused')
srv.shutdown()
# ---------------- federation ----------------
ok(requests.get(B2+'/federation/summary').status_code==401,'instance summary needs the federation key')
pf=mg.get(B+'/federation/portfolio').json(); inst={x['name']:x for x in pf['instances']}
ok(pf['self']['instance']=='Maharashtra' and len(pf['self']['dams'])>=2,f"hub includes its own portfolio ({len(pf['self']['dams'])} dams)")
ok(inst['Kerala']['ok'] and inst['Kerala']['summary']['dams'][0]['code']=='DM-001',f"Kerala instance aggregated ({inst['Kerala']['latencyMs']} ms)")
ok(not inst['Wrong key']['ok'] and '401' in inst['Wrong key']['error'],'instance with a wrong key reported, not silently skipped')
ok(ins.get(B+'/federation/portfolio').status_code==403,'inspector cannot see the federated portfolio')
print('ALL PHASE 7 TESTS PASSED')
