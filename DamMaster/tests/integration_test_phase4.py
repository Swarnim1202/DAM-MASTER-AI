"""Phase 4 end-to-end checks: MQTT ingestion, idempotency, gateway health, buffered data, aggregates, scheduled refit.
Needs: API started with Mqtt:Enabled=true against a local broker; run after the Phase 1–3 tests.
DM_MQTT_HOST / DM_MQTT_PORT (default 127.0.0.1:1883), DM_MYSQL_CMD for time-shift checks."""
import os, sys, json, time, uuid, subprocess, datetime as dt, requests
import paho.mqtt.client as mqtt
B=os.environ.get('DM_API','http://localhost:5080')+'/api'; H={'X-Requested-With':'DamMaster'}
MH=os.environ.get('DM_MQTT_HOST','127.0.0.1'); MP=int(os.environ.get('DM_MQTT_PORT','1883')); SQL=os.environ.get('DM_MYSQL_CMD')
def S(): s=requests.Session(); s.headers.update(H); return s
def login(u,p):
  s=S(); r=s.post(B+'/auth/login',json={'username':u,'password':p}); assert r.status_code==200,(u,r.text); return s
def ok(c,m):
  print(('PASS ' if c else 'FAIL ')+m)
  if not c: sys.exit(1)
def sql(q): subprocess.run(SQL.split()+['-e',q],check=True)
adm=login('admin','Admin@2026x'); dse=login('arjun','Temp@2026'); vik=login('vikram','Temp@2026'); ins=login('meera','Temp@2026')
st=adm.get(B+'/system/status').json(); ok(st['mqtt']['enabled'] and st['mqtt']['connected'],f"API connected to MQTT broker {st['mqtt']['broker']}")
dam=[d for d in dse.get(B+'/dams').json() if d['code']=='DM-900'][0]; D=dam['id']
g=vik.post(B+'/gateways',json={'code':'GW-DM900-MQ','damId':D,'expectedIntervalMin':60}).json(); G=g['id']
ok(g['mqtt']['telemetryTopic']=='dammaster/DM-900/GW-DM900-MQ/telemetry','registration returns the MQTT topics')
acks=[]
cl=mqtt.Client(mqtt.CallbackAPIVersion.VERSION2,client_id='test-gw-'+uuid.uuid4().hex[:6])
cl.on_message=lambda c,u,m: acks.append(json.loads(m.payload))
cl.connect(MH,MP); cl.subscribe('dammaster/+/GW-DM900-MQ/ack',qos=1); cl.loop_start(); time.sleep(0.5)
IST=dt.timezone(dt.timedelta(hours=5,minutes=30))
def iso(t): return t.astimezone(IST).strftime('%Y-%m-%dT%H:%M:00+05:30')
now=dt.datetime.now(dt.timezone.utc).replace(second=0,microsecond=0)
def msg(mid,times,val=130.0,dam='DM-900',gw='GW-DM900-MQ'):
  return {'schemaVersion':'1.0','messageId':mid,'gatewayId':gw,'damId':dam,'readings':[r for t in times for r in (
    {'sensorId':'DM900-RL-01','parameterCode':'RWL','observedAt':iso(t),'value':650.0,'unit':'m','loggerStatus':'OK'},
    {'sensorId':'DM900-PZ-01','parameterCode':'UPL','observedAt':iso(t),'value':val,'unit':'kPa','loggerStatus':'OK'})]}
def pub(topic,body): cl.publish(topic,json.dumps(body),qos=1).wait_for_publish(5)
def wait_ack(n,timeout=15):
  t0=time.time()
  while len(acks)<n and time.time()-t0<timeout: time.sleep(0.2)
  return len(acks)>=n
T='dammaster/DM-900/GW-DM900-MQ/telemetry'
pub(T,msg('mq-1',[now-dt.timedelta(minutes=30),now])); ok(wait_ack(1),'telemetry over MQTT acknowledged')
ok(acks[0]['accepted']==4 and not acks[0]['duplicateMessage'],f"4 readings accepted ({acks[0]})")
pub(T,msg('mq-1',[now-dt.timedelta(minutes=30),now])); ok(wait_ack(2) and acks[1]['duplicateMessage'],'redelivered message (same messageId) ignored — idempotent')
pub('dammaster/DM-001/GW-DM900-MQ/telemetry',msg('mq-x',[now],dam='DM-001')); ok(wait_ack(3) and 'error' in acks[2] and acks[2]['error'],'message under another dam is refused')
pub('dammaster/DM-900/GW-DM900-MQ/status',{'gatewayId':'GW-DM900-MQ','batteryV':12.6,'signalDbm':-71,'bufferedCount':0,'uptimeS':86400,'firmware':'2.3.1'}); time.sleep(1.5)
gh=[x for x in vik.get(B+'/gateways').json() if x['id']==G][0]
ok(gh['batteryV']==12.6 and gh['signalDbm']==-71 and gh['firmware']=='2.3.1' and gh['transport']=='MQTT','status message updates battery, signal, firmware')
ok(gh['state']=='Online' and gh['messages24h']==1,f"gateway Online, {gh['messages24h']} messages in 24 h")
# buffered data after an outage: 3-day-old readings arrive now
old=[now-dt.timedelta(days=3,hours=h) for h in (6,12,18)]
pub(T,msg('mq-2',old)); ok(wait_ack(4) and acks[3]['accepted']==6 and acks[3]['late']==6,f"buffered readings from 3 days ago accepted and counted as late ({acks[3]['late']})")
rec=vik.get(B+f'/gateways/{G}/reconciliation?days=7').json(); r={x['code']:x for x in rec['rows']}
ok(r['DM900-PZ-01']['backfilled']>=3,f"reconciliation shows {r['DM900-PZ-01']['backfilled']} back-filled PZ readings")
# HTTP path is idempotent too
hk=vik.post(B+f'/gateways/{G}/rotate-key').json()['apiKey']
hm=msg('http-1',[now-dt.timedelta(minutes=10)]); hd={'X-Gateway-Code':'GW-DM900-MQ','X-Api-Key':hk}
r1=requests.post(B+'/telemetry',json=hm,headers=hd); r2=requests.post(B+'/telemetry',json=hm,headers=hd)
ok(r1.status_code==202 and r2.status_code==200 and r2.json()['duplicateMessage'],'HTTP retry with the same messageId is ignored')
ok(ins.post(B+f'/gateways/{G}/rotate-key').status_code==403,'inspector cannot rotate gateway keys')
if SQL:
  sql(f"UPDATE gateway SET last_message_at=DATE_SUB(UTC_TIMESTAMP(), INTERVAL 5 HOUR), last_seen_at=DATE_SUB(UTC_TIMESTAMP(), INTERVAL 5 HOUR) WHERE id='{G}'")
  adm.post(B+'/system/run-jobs')
  al=[a for a in dse.get(B+f'/alerts?damId={D}').json() if a.get('gatewayId')==G and a['ruleType']=='Gateway']
  ok(len(al)==1 and al[0]['level']==1,'gateway silent 5 h (expected hourly) → Level 1 Gateway offline alert')
  ok([x for x in vik.get(B+'/gateways').json() if x['id']==G][0]['state']=='Offline','dashboard shows Offline')
  pub(T,msg('mq-3',[now+dt.timedelta(minutes=1)])); ok(wait_ack(5),'gateway reconnects and sends data')
  a=dse.get(B+f"/alerts/{al[0]['id']}").json()['alert']; ok(a['status']=='Closed' and a['disposition']=='Resolved','offline alert resolved automatically when data returns')
  pub('dammaster/DM-900/GW-DM900-MQ/status',{'gatewayId':'GW-DM900-MQ','batteryV':10.9}); time.sleep(1.5); adm.post(B+'/system/run-jobs')
  ok([a for a in dse.get(B+f'/alerts?damId={D}').json() if a.get('gatewayId')==G and a['ruleType']=='GatewayBattery'],'battery 10.9 V → low-battery alert')
  # scheduled HST refit
  sql("UPDATE hst_model m JOIN sensor s ON s.id=m.sensor_id SET m.fitted_at=DATE_SUB(UTC_TIMESTAMP(), INTERVAL 40 DAY), m.data_to=DATE_SUB(m.data_to, INTERVAL 60 DAY) WHERE s.code='DM900-PZ-01' AND m.status='Active'")
  res=adm.post(B+'/system/run-jobs').json(); ok('1 refitted' in res['HstRefit'],f"scheduled refit job: {res['HstRefit']}")
  pz=[s['id'] for s in dse.get(B+f'/dams/{D}/sensors').json() if s['code']=='DM900-PZ-01'][0]
  ms=dse.get(B+f'/sensors/{pz}/hst').json()['models']; ok(ms[0]['status']=='Draft' and ms[0]['fittedBy']=='system (scheduled refit)','new draft model waiting for review (old model stays active)')
  inbox=dse.get(B+'/notifications/mine').json(); ok(any('refit ready' in n['subject'] for n in inbox),'dam safety engineer notified in-app about the refit')
else: print('SKIP time-shift checks (DM_MYSQL_CMD not set)')
# aggregates
pz=[s['id'] for s in dse.get(B+f'/dams/{D}/sensors').json() if s['code']=='DM900-PZ-01'][0]
daily=dse.get(B+f'/sensors/{pz}/readings?days=0&agg=daily').json(); ok(700<=len(daily)<=740 and all(p['q']=='D' for p in daily),f"daily aggregates: {len(daily)} points for 2 years")
st=adm.get(B+'/system/status').json(); ok(st['mqtt']['received']>=5 and st['mqtt']['duplicateMessages']>=1,f"MQTT counters: {st['mqtt']['received']} received, {st['mqtt']['duplicateMessages']} duplicate")
ok(any(j['job']=='GatewayHealth' for j in st['jobs']) and st['storage']['readings']>0,'job runs and storage figures reported')
cl.loop_stop(); cl.disconnect()
print('ALL PHASE 4 TESTS PASSED')
