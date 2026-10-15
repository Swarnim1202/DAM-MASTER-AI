import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { useToast } from '../components/ui';
import * as off from '../lib/offline';

interface CheckItem { code: string; component: string; question: string; result?: string | null; remark?: string | null }
interface Pkg { inspection: { id: string; damName?: string; inspectionType: string; scheduledDate: string; status: string }; dam: { id: string; code: string; name: string }; checklist: CheckItem[]; findings: { id: string; component: string; severity: string; description: string }[]; sensors: { id: string; code: string; location?: string }[]; packagedAt: string; local?: { status: string; findings: { ref: string; component: string; severity: string; description: string; photos: number }[] } }
const COMPONENTS = ['Upstream face', 'Crest & parapet', 'Downstream face / slope', 'Galleries', 'Spillway & gates', 'Abutments', 'Downstream area', 'Instruments', 'Other'];

/** Header shown on every field screen: connection, queued items, sync. */
function useSyncBar() {
  const toast = useToast(); const [online, setOnline] = useState(navigator.onLine); const [pending, setPending] = useState(0); const [busy, setBusy] = useState(false); const [issues, setIssues] = useState<off.SyncIssue[]>([]);
  const refresh = useCallback(async () => { setPending((await off.queuedOps()).length + (await off.queuedPhotos()).length); setIssues(await off.issues()); }, []);
  const doSync = useCallback(async () => {
    if (!navigator.onLine) { toast('Offline — items stay on this device until the connection returns.'); return; }
    setBusy(true); try { const r = await off.sync(); toast(`Synced: ${r.applied} change(s), ${r.photos} photo(s)${r.rejected ? `, ${r.rejected} rejected — see below` : ''}.`); } catch (e) { toast((e as Error).message); } finally { setBusy(false); void refresh(); }
  }, [refresh, toast]);
  useEffect(() => { void refresh(); const on = () => { setOnline(true); void doSync(); }; const offl = () => setOnline(false); window.addEventListener('online', on); window.addEventListener('offline', offl); return () => { window.removeEventListener('online', on); window.removeEventListener('offline', offl); }; }, [doSync, refresh]);
  const bar = (
    <div className="card" style={{ position: 'sticky', top: 0, zIndex: 5, display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap', padding: '10px 12px' }}>
      <span className={`badge ${online ? 'b-0' : 'b-1'}`}>{online ? 'Online' : 'Offline'}</span>
      <span className="small">{pending} item(s) waiting to sync</span>
      <button className="btn sm primary" style={{ marginLeft: 'auto' }} disabled={busy || pending === 0} onClick={doSync}>{busy ? 'Syncing…' : 'Sync now'}</button>
      {issues.length > 0 && <div className="note small" style={{ width: '100%' }}>{issues.map((x, i) => <div key={i}><b>{x.what}</b>: {x.error}</div>)}<button className="btn sm" onClick={async () => { await off.clearIssues(); void refresh(); }}>Clear</button></div>}
    </div>);
  return { bar, refresh };
}

export function FieldHome() {
  const { bar } = useSyncBar(); const toast = useToast(); const nav = useNavigate();
  const [remote, setRemote] = useState<{ id: string; damName?: string; inspectionType: string; scheduledDate: string; status: string }[] | null>(null); const [local, setLocal] = useState<Pkg[]>([]);
  const load = useCallback(async () => { setLocal(await off.allPackages<Pkg>()); if (navigator.onLine) try { setRemote(await api.get('/field/inspections')); } catch { setRemote(null); } }, []);
  useEffect(() => { void load(); }, [load]);
  const download = async (id: string) => { try { await off.putPackage(id, await api.get<Pkg>(`/field/package/${id}`)); toast('Saved on this device — works without signal.'); void load(); } catch (e) { toast((e as Error).message); } };
  return (
    <div style={{ maxWidth: 720, margin: '0 auto' }}>
      {bar}
      <h1 style={{ fontSize: 22 }}>Field inspections</h1>
      <p className="muted small">Download an inspection before going to the dam. Checklist, findings and photos are kept on this device and sent when there is signal. <Link to="/">Desk view →</Link></p>
      <h3>On this device</h3>
      {local.length === 0 && <p className="muted">Nothing downloaded yet.</p>}
      {local.map((p) => <div key={p.inspection.id} className="card" style={{ marginBottom: 8 }} onClick={() => nav('/field/' + p.inspection.id)} role="button">
        <b>{p.dam.name}</b> · {p.inspection.inspectionType}<div className="small muted">{p.inspection.scheduledDate.slice(0, 10)} · {p.local?.status ?? p.inspection.status} · {p.checklist.filter((c) => c.result).length}/{p.checklist.length} checked</div></div>)}
      <h3>Open inspections {remote === null && <span className="muted small">(offline — list unavailable)</span>}</h3>
      {remote?.map((i) => <div key={i.id} className="card row" style={{ marginBottom: 8 }}><div style={{ flex: 1 }}><b>{i.damName}</b> · {i.inspectionType}<div className="small muted">{i.scheduledDate.slice(0, 10)} · {i.status}</div></div>
        <button className="btn sm" onClick={() => download(i.id)}>{local.some((l) => l.inspection.id === i.id) ? 'Refresh' : 'Download'}</button></div>)}
    </div>
  );
}

export function FieldInspection() {
  const { id } = useParams(); const { bar, refresh } = useSyncBar(); const toast = useToast(); const { user } = useAuth();
  const [p, setP] = useState<Pkg | null>(null); const [f, setF] = useState({ component: 'Galleries', location: '', description: '', severity: 'Medium', recommendation: '', sensorId: '' }); const [shots, setShots] = useState<File[]>([]);
  const [done, setDone] = useState({ overallCondition: 'Good', summary: '' });
  useEffect(() => { void off.getPackage<Pkg>(id!).then((x) => setP(x ?? null)); }, [id]);
  if (!p) return <div style={{ maxWidth: 720, margin: '0 auto' }}>{bar}<p>Not on this device. <Link to="/field">Back</Link></p></div>;
  const status = p.local?.status ?? p.inspection.status; const save = async (np: Pkg) => { setP(np); await off.putPackage(id!, np); void refresh(); };
  const op = async (kind: off.QueuedOp['kind'], payload: unknown, label: string, ref = off.newId()) => { await off.queueOp({ clientOpId: ref, kind, inspectionId: id!, payload, createdAt: new Date().toISOString(), label }); return ref; };
  const start = async () => { await op('start', {}, 'Start inspection'); await save({ ...p, local: { status: 'In progress', findings: p.local?.findings ?? [] } }); toast('Started — recorded on this device.'); };
  const setItem = (code: string, k: 'result' | 'remark', v: string) => setP({ ...p, checklist: p.checklist.map((c) => (c.code === code ? { ...c, [k]: v } : c)) });
  const saveChecks = async () => {
    const rec = p.checklist.filter((c) => c.result); if (rec.some((c) => c.result === 'Attention' && !c.remark?.trim())) { toast('Add a remark for every item marked Attention.'); return; }
    await op('checks', rec.map((c) => ({ itemCode: c.code, result: c.result, remark: c.remark || null })), `Checklist (${rec.length} items)`); await save(p); toast(`${rec.length} item(s) saved on the device.`);
  };
  const addFinding = async () => {
    if (!f.description.trim()) { toast('Describe what you see.'); return; }
    const ref = await op('finding', { ...f, sensorId: f.sensorId || null }, `${f.severity} finding: ${f.component}`);
    const pos = await off.position();
    for (const [k, file] of shots.entries()) await off.queuePhoto({ clientRef: `${ref}-p${k}`, findingRef: ref, inspectionId: id!, blob: await off.shrinkPhoto(file), fileName: file.name || `photo-${k + 1}.jpg`, latitude: pos?.coords.latitude, longitude: pos?.coords.longitude, takenAt: new Date().toISOString(), caption: f.location || undefined });
    await save({ ...p, local: { status, findings: [...(p.local?.findings ?? []), { ref, component: f.component, severity: f.severity, description: f.description, photos: shots.length }] } });
    setF({ ...f, location: '', description: '', recommendation: '', sensorId: '' }); setShots([]); toast(`Finding saved with ${shots.length} photo(s)${pos ? ' and GPS' : ''}.`);
  };
  const complete = async () => {
    const missing = p.checklist.filter((c) => !c.result).length; if (missing) { toast(`${missing} checklist item(s) not yet recorded.`); return; }
    await op('complete', done, 'Complete inspection'); await save({ ...p, local: { status: 'Completed', findings: p.local?.findings ?? [] } }); toast('Completed — sync to send it.');
  };
  const sevClass: Record<string, string> = { Low: 'b-mute', Medium: 'b-1', High: 'b-2', Critical: 'b-3' };
  return (
    <div style={{ maxWidth: 720, margin: '0 auto' }}>
      {bar}
      <p className="small"><Link to="/field">← Field inspections</Link></p>
      <h1 style={{ fontSize: 21, marginBottom: 2 }}>{p.dam.name}</h1>
      <p className="muted small" style={{ marginTop: 0 }}>{p.inspection.inspectionType} · {p.inspection.scheduledDate.slice(0, 10)} · <b>{status}</b> · inspector {user?.fullName}</p>
      {status === 'Planned' && <button className="btn primary" style={{ width: '100%', padding: 14 }} onClick={start}>Start inspection</button>}
      {status === 'In progress' && <>
        <h3>Checklist</h3>
        {p.checklist.map((c) => <div key={c.code} className="card" style={{ marginBottom: 6, padding: 10 }}>
          <div className="small muted">{c.component}</div><div>{c.question}</div>
          <div className="row" style={{ marginTop: 6 }}>{['OK', 'Attention', 'Not applicable'].map((r) => <button key={r} className={`btn sm ${c.result === r ? (r === 'Attention' ? 'danger' : 'primary') : ''}`} style={{ flex: 1, padding: 10 }} onClick={() => setItem(c.code, 'result', r)}>{r === 'Not applicable' ? 'N/A' : r}</button>)}</div>
          {c.result === 'Attention' && <input className="input" style={{ marginTop: 6 }} placeholder="What needs attention?" value={c.remark ?? ''} onChange={(e) => setItem(c.code, 'remark', e.target.value)} />}</div>)}
        <button className="btn primary" style={{ width: '100%', padding: 12 }} onClick={saveChecks}>Save checklist</button>
        <h3>Add finding</h3>
        <div className="card form">
          <label className="small">Component<select className="input" value={f.component} onChange={(e) => setF({ ...f, component: e.target.value })}>{COMPONENTS.map((x) => <option key={x}>{x}</option>)}</select></label>
          <label className="small">Location<input className="input" value={f.location} onChange={(e) => setF({ ...f, location: e.target.value })} placeholder="e.g. Gallery joint 14/15" /></label>
          <label className="small">What you see<textarea className="input" value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} /></label>
          <label className="small">Severity<div className="row">{['Low', 'Medium', 'High', 'Critical'].map((s) => <button key={s} className={`btn sm ${f.severity === s ? 'primary' : ''}`} style={{ flex: 1, padding: 10 }} onClick={() => setF({ ...f, severity: s })}>{s}</button>)}</div></label>
          <label className="small">Related instrument<select className="input" value={f.sensorId} onChange={(e) => setF({ ...f, sensorId: e.target.value })}><option value="">—</option>{p.sensors.map((s) => <option key={s.id} value={s.id}>{s.code}{s.location ? ` · ${s.location}` : ''}</option>)}</select></label>
          <label className="small">Recommendation<input className="input" value={f.recommendation} onChange={(e) => setF({ ...f, recommendation: e.target.value })} /></label>
          <label className="btn" style={{ textAlign: 'center', padding: 12 }}>📷 Take / add photos ({shots.length})<input type="file" accept="image/*" capture="environment" multiple hidden onChange={(e) => setShots([...shots, ...Array.from(e.target.files ?? [])])} /></label>
          <button className="btn primary" style={{ padding: 12 }} onClick={addFinding}>Save finding</button>
          <p className="muted small" style={{ margin: 0 }}>High and Critical findings raise a dam alert as soon as they sync. Photos are reduced to 1600 px before storing.</p></div>
        {(p.local?.findings.length ?? 0) > 0 && <><h3>Findings recorded here</h3>{p.local!.findings.map((x) => <div key={x.ref} className="card small" style={{ marginBottom: 6 }}><span className={`badge ${sevClass[x.severity]}`}>{x.severity}</span> <b>{x.component}</b> — {x.description} · {x.photos} photo(s)</div>)}</>}
        <h3>Finish</h3>
        <div className="card form"><label className="small">Overall condition<select className="input" value={done.overallCondition} onChange={(e) => setDone({ ...done, overallCondition: e.target.value })}>{['Good', 'Fair', 'Poor', 'Unsatisfactory'].map((x) => <option key={x}>{x}</option>)}</select></label>
          <label className="small">Summary<textarea className="input" value={done.summary} onChange={(e) => setDone({ ...done, summary: e.target.value })} /></label>
          <button className="btn green" style={{ padding: 12 }} onClick={complete}>Complete inspection</button></div>
      </>}
      {status === 'Completed' && <p className="note">Completed on this device. Sync to send it — it is then visible in the desk view and counts for monsoon compliance.</p>}
    </div>
  );
}
