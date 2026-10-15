import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, fmtNum, fmtTime } from '../api/client';
import { useApi, useCatalog } from '../api/hooks';
import type { Alert, Dam, Pfma, RefItem, Sensor, SensorStatus } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBox, Field, LevelBadge, Modal, QualityBadge, num, useToast } from '../components/ui';
import { DamForm } from './Dams';
import { AlertTable } from './Alerts';
import { EapTab, InspectionsTab, ScheduleTab } from './DamPhase2Tabs';
import { DhiTab, DocumentsTab } from './DamPhase3Tabs';
import { DesignLinkTab, TwinTab } from './DamPhase6Tabs';
import { lazy, Suspense } from 'react';
const Twin3D = lazy(() => import('../components/Twin3D'));

export function SensorForm({ damId, initial, onClose, onSaved }: { damId: string; initial?: Sensor; onClose: () => void; onSaved: () => void }) {
  const params = useCatalog<RefItem & { unit: string }>('parameters'); const instr = useCatalog<RefItem>('instrument-types');
  const [s, setS] = useState<Sensor>(initial ?? { id: '', damId, code: '', parameterCode: 'UPL', engUnit: 'kPa', direction: 'Rising', readingIntervalMin: 360, status: 'Active' });
  const [err, setErr] = useState<string | null>(null);
  const set = (k: keyof Sensor, v: unknown) => setS({ ...s, [k]: v });
  const save = async () => { try { if (s.id) await api.put('/sensors/' + s.id, s); else await api.post('/sensors', s); onSaved(); } catch (e) { setErr((e as Error).message); } };
  return (
    <Modal title={s.id ? `Edit ${s.code}` : 'Register sensor'} onClose={onClose} wide footer={<><button className="btn" onClick={onClose}>Cancel</button><button className="btn primary" onClick={save}>Save</button></>}>
      <ErrorBox error={err} />
      <div className="form">
        <Field label="Sensor code"><input className="input" value={s.code} disabled={!!s.id} onChange={(e) => set('code', e.target.value)} placeholder="DM001-PZ-07" /></Field>
        <Field label="Parameter"><select className="input" value={s.parameterCode} onChange={(e) => { const p = params.find((x) => x.code === e.target.value); setS({ ...s, parameterCode: e.target.value, engUnit: p?.unit ?? s.engUnit }); }}>{params.map((p) => <option key={p.code} value={p.code}>{p.code} — {p.name}</option>)}</select></Field>
        <Field label="Instrument type"><select className="input" value={s.instrumentTypeCode ?? ''} onChange={(e) => set('instrumentTypeCode', e.target.value || null)}><option value="">—</option>{instr.map((p) => <option key={p.code} value={p.code}>{p.name}</option>)}</select></Field>
        <Field label="Location"><input className="input" value={s.location ?? ''} onChange={(e) => set('location', e.target.value)} /></Field>
        <Field label="Elevation (m)"><input className="input" type="number" step="any" value={s.elevationM ?? ''} onChange={(e) => set('elevationM', num(e.target.value))} /></Field>
        <Field label="Engineering unit"><input className="input" value={s.engUnit} onChange={(e) => set('engUnit', e.target.value)} /></Field>
        <Field label="Expected minimum"><input className="input" type="number" step="any" value={s.expectedMin ?? ''} onChange={(e) => set('expectedMin', num(e.target.value))} /></Field>
        <Field label="Expected maximum"><input className="input" type="number" step="any" value={s.expectedMax ?? ''} onChange={(e) => set('expectedMax', num(e.target.value))} /></Field>
        <Field label="Danger direction"><select className="input" value={s.direction} onChange={(e) => set('direction', e.target.value)}><option>Rising</option><option>Falling</option></select></Field>
        <Field label="Reading interval (min)"><input className="input" type="number" value={s.readingIntervalMin} onChange={(e) => set('readingIntervalMin', Number(e.target.value) || 360)} /></Field>
        <Field label="Calibration due"><input className="input" type="date" value={(s.calibrationDue ?? '').slice(0, 10)} onChange={(e) => set('calibrationDue', e.target.value || null)} /></Field>
        <Field label="Status"><select className="input" value={s.status} onChange={(e) => set('status', e.target.value)}>{['Active', 'Faulty', 'Under maintenance', 'Retired'].map((x) => <option key={x}>{x}</option>)}</select></Field>
        <Field label="Latitude"><input className="input" type="number" step="any" value={s.latitude ?? ''} onChange={(e) => set('latitude', num(e.target.value))} /></Field>
        <Field label="Longitude"><input className="input" type="number" step="any" value={s.longitude ?? ''} onChange={(e) => set('longitude', num(e.target.value))} /></Field>
        <Field label="Offset from heel / upstream face (m)"><input className="input" type="number" step="any" value={s.offsetM ?? ''} onChange={(e) => set('offsetM', num(e.target.value))} /></Field>
        <Field label="Chainage along axis (m)"><input className="input" type="number" step="any" value={s.chainageM ?? ''} onChange={(e) => set('chainageM', num(e.target.value))} /></Field>
      </div>
    </Modal>
  );
}

function PfmaForm({ dam, sensors, initial, onClose, onSaved }: { dam: Dam; sensors: SensorStatus[]; initial?: Pfma; onClose: () => void; onSaved: () => void }) {
  const fms = useCatalog<RefItem & { description?: string; earlySigns?: string }>('failure-modes'); const fmp = useCatalog<{ failureModeCode: string; parameterCode: string; role: string }>('failure-mode-parameters');
  const [p, setP] = useState<Pfma>(initial ?? { id: '', damId: dam.id, failureModeCode: 'PIPING', status: 'Active', createdAt: new Date().toISOString(), sensorIds: [] });
  const [err, setErr] = useState<string | null>(null);
  const fm = fms.find((f) => f.code === p.failureModeCode);
  const related = new Set(fmp.filter((x) => x.failureModeCode === p.failureModeCode).map((x) => x.parameterCode));
  const toggle = (id: string) => setP({ ...p, sensorIds: p.sensorIds.includes(id) ? p.sensorIds.filter((x) => x !== id) : [...p.sensorIds, id] });
  const save = async () => { try { if (p.id) await api.put('/pfma/' + p.id, p); else await api.post('/pfma', p); onSaved(); } catch (e) { setErr((e as Error).message); } };
  return (
    <Modal title={p.id ? 'Edit failure mode' : 'Add potential failure mode (PFMA)'} onClose={onClose} wide footer={<><button className="btn" onClick={onClose}>Cancel</button><button className="btn primary" onClick={save}>Save</button></>}>
      <ErrorBox error={err} />
      <div className="form two">
        <Field label="Failure mode"><select className="input" value={p.failureModeCode} onChange={(e) => setP({ ...p, failureModeCode: e.target.value })}>{fms.map((f) => <option key={f.code} value={f.code}>{f.name}</option>)}</select></Field>
        <Field label="Location"><input className="input" value={p.location ?? ''} onChange={(e) => setP({ ...p, location: e.target.value })} placeholder="e.g. Left abutment, block 12 foundation" /></Field>
        <Field label="Likelihood"><select className="input" value={p.likelihood ?? ''} onChange={(e) => setP({ ...p, likelihood: e.target.value })}><option value="">—</option>{['Remote', 'Possible', 'Likely'].map((x) => <option key={x}>{x}</option>)}</select></Field>
        <Field label="Consequence"><select className="input" value={p.consequence ?? ''} onChange={(e) => setP({ ...p, consequence: e.target.value })}><option value="">—</option>{['Low', 'Significant', 'High'].map((x) => <option key={x}>{x}</option>)}</select></Field>
      </div>
      <Field label="Description (how it would develop)"><textarea className="input" value={p.description ?? ''} onChange={(e) => setP({ ...p, description: e.target.value })} /></Field>
      {fm && <p className="note"><b>{fm.name}:</b> {fm.description} <br /><b>Early signs:</b> {fm.earlySigns}</p>}
      <h4>Instruments that watch this failure mode</h4>
      <p className="muted small">Two or more linked instruments abnormal together raise a corroboration alert (at least Level 2). Recommended parameters are marked ★.</p>
      <div className="tablewrap" style={{ maxHeight: 260 }}><table><tbody>{sensors.map((s) => (
        <tr key={s.id}><td><input type="checkbox" checked={p.sensorIds.includes(s.id)} onChange={() => toggle(s.id)} aria-label={'Link ' + s.code} /></td>
          <td className="mono">{s.code}</td><td>{related.has(s.parameterCode) ? '★ ' : ''}{s.parameterName}</td><td className="muted">{s.location}</td></tr>))}</tbody></table></div>
    </Modal>
  );
}

export default function DamDetail() {
  const { id } = useParams(); const nav = useNavigate(); const { can } = useAuth(); const toast = useToast();
  const dam = useApi<Dam>('/dams/' + id); const sensors = useApi<SensorStatus[]>(`/dams/${id}/sensors`); const pfma = useApi<Pfma[]>(`/dams/${id}/pfma`);
  const alerts = useApi<Alert[]>(`/alerts?damId=${id}&status=All&take=300`);
  const fms = useCatalog<RefItem>('failure-modes');
  const [tab, setTab] = useState<'sensors' | 'pfma' | 'alerts' | 'schedule' | 'inspections' | 'eap' | 'dhi' | 'documents' | 'twin' | 'design' | 'model3d'>('dhi');
  const [editDam, setEditDam] = useState(false); const [newSensor, setNewSensor] = useState(false); const [pf, setPf] = useState<Pfma | null | undefined>(undefined);
  const d = dam.data;
  const evaluate = async () => { try { const r = await api.post<Alert[]>(`/dams/${id}/evaluate`); toast(`${r.length} alert(s) raised or updated.`); void alerts.reload(); void sensors.reload(); } catch (e) { toast((e as Error).message); } };
  if (!d) return <ErrorBox error={dam.error} />;
  return (
    <>
      <div className="hero"><div><h1>{d.name}</h1><p>{d.code} · {d.damTypeCode} · {d.hazardCode} hazard · {d.state ?? ''} · {d.lifePhase}</p></div>
        <div className="row">{can('evaluate') && <button className="btn" onClick={evaluate}>↻ Re-evaluate alerts</button>}{can('manageDams') && <button className="btn" onClick={() => setEditDam(true)}>Edit dam</button>}</div></div>
      <div className="grid g4">
        <div className="card"><div className="kv"><span>Height</span><b>{d.heightM ?? '—'} m</b><span>Top of dam</span><b>{d.crestLevelM ?? '—'} m</b><span>FRL / MWL / MDDL</span><b>{d.frlM ?? '—'} / {d.mwlM ?? '—'} / {d.mddlM ?? '—'} m</b></div></div>
        <div className="card"><div className="kv"><span>Seismic zone</span><b>{d.seismicZone ?? '—'}</b><span>Specified dam</span><b>{d.isSpecified ? 'Yes' : 'No'}</b><span>EAP</span><b>{d.eapStatus ?? '—'}</b></div></div>
        <div className="card"><div className="kv"><span>Sensors</span><b>{sensors.data?.length ?? '—'}</b><span>Open alerts</span><b>{alerts.data?.filter((a) => a.status !== 'Closed').length ?? '—'}</b><span>Failure modes</span><b>{pfma.data?.length ?? '—'}</b></div></div>
        <div className="card"><div className="kv"><span>Highest open level</span><b><LevelBadge level={Math.max(0, ...(alerts.data ?? []).filter((a) => a.status !== 'Closed').map((a) => a.level))} /></b><span>Owner</span><b>{d.ownerName ?? '—'}</b></div></div>
      </div>
      <div className="card mt">
        <div className="tabs" role="tablist">{([['dhi', 'Health (DHI)'], ['twin', 'Live section'], ['model3d', '3-D view'], ['sensors', 'Sensors'], ['pfma', 'Failure modes (PFMA)'], ['alerts', 'Alerts'], ['schedule', 'Reading schedule'], ['inspections', 'Inspections'], ['eap', 'EAP contacts'], ['documents', 'Documents'], ['design', 'Design link']] as const).map(([t, l]) => <button key={t} role="tab" aria-selected={tab === t} className={tab === t ? 'on' : ''} onClick={() => setTab(t)}>{l}</button>)}</div>
        {tab === 'sensors' && <>
          <div className="row" style={{ justifyContent: 'flex-end', marginBottom: 8 }}>{can('manageSensors') && <button className="btn primary sm" onClick={() => setNewSensor(true)}>+ Register sensor</button>}</div>
          <div className="tablewrap"><table>
            <thead><tr><th>Sensor</th><th>Parameter</th><th>Location</th><th>Latest</th><th>Time (IST)</th><th>Flag</th><th>Thresholds A / Al / E</th><th>Status</th><th>Readings</th></tr></thead>
            <tbody>{sensors.data?.map((s) => (
              <tr key={s.id} className="click" onClick={() => nav('/sensors/' + s.id)}>
                <td className="mono"><b>{s.code}</b></td><td>{s.parameterName}</td><td>{s.location}</td><td>{fmtNum(s.latestValue)} {s.engUnit}</td><td>{fmtTime(s.latestAt)}</td><td><QualityBadge q={s.latestQuality} /></td>
                <td>{s.alertValue ?? '—'} / {s.alarmValue ?? '—'} / {s.emergencyValue ?? '—'} {s.alertValue != null && !s.thresholdApproved && <span className="badge b-1">pending approval</span>}</td>
                <td>{s.openAlerts ? <LevelBadge level={s.maxOpenLevel} /> : <span className="badge b-0">Normal</span>}</td><td>{s.readingCount}</td></tr>))}
              {sensors.data?.length === 0 && <tr><td colSpan={9} className="empty">No sensors. Register one or <Link to="/import">import the template</Link>.</td></tr>}</tbody></table></div></>}
        {tab === 'pfma' && <>
          <div className="row" style={{ justifyContent: 'space-between', marginBottom: 8 }}><span className="muted small">How could this dam fail, and which instruments would show it first?</span>{can('managePfma') && <button className="btn primary sm" onClick={() => setPf(null)}>+ Add failure mode</button>}</div>
          <div className="tablewrap"><table><thead><tr><th>Failure mode</th><th>Location</th><th>Likelihood / consequence</th><th>Linked instruments</th><th>Status</th><th></th></tr></thead>
            <tbody>{pfma.data?.map((p) => (
              <tr key={p.id}><td><b>{fms.find((f) => f.code === p.failureModeCode)?.name ?? p.failureModeCode}</b><div className="muted small">{p.description}</div></td><td>{p.location}</td><td>{p.likelihood ?? '—'} / {p.consequence ?? '—'}</td>
                <td className="mono small">{p.sensorIds.map((sid) => sensors.data?.find((s) => s.id === sid)?.code).filter(Boolean).join(', ') || '—'}</td><td>{p.status}</td>
                <td>{can('managePfma') && <button className="btn sm" onClick={() => setPf(p)}>Edit</button>}</td></tr>))}
              {pfma.data?.length === 0 && <tr><td colSpan={6} className="empty">No failure modes recorded. Start with the PFMA workshop results.</td></tr>}</tbody></table></div></>}
        {tab === 'dhi' && <DhiTab damId={d.id} />}
        {tab === 'twin' && <TwinTab damId={d.id} />}
        {tab === 'model3d' && <Suspense fallback={<p className="muted">Loading 3-D view…</p>}><Twin3D damId={d.id} /></Suspense>}
        {tab === 'design' && <DesignLinkTab damId={d.id} />}
        {tab === 'documents' && <DocumentsTab damId={d.id} />}
        {tab === 'schedule' && <ScheduleTab damId={d.id} />}
        {tab === 'inspections' && <InspectionsTab damId={d.id} />}
        {tab === 'eap' && <EapTab damId={d.id} />}
        {tab === 'alerts' && <AlertTable alerts={alerts.data ?? []} onChanged={() => { void alerts.reload(); void sensors.reload(); }} />}
      </div>
      {editDam && <DamForm initial={d} onClose={() => setEditDam(false)} onSaved={() => { setEditDam(false); void dam.reload(); toast('Dam updated.'); }} />}
      {newSensor && <SensorForm damId={d.id} onClose={() => setNewSensor(false)} onSaved={() => { setNewSensor(false); void sensors.reload(); toast('Sensor registered.'); }} />}
      {pf !== undefined && sensors.data && <PfmaForm dam={d} sensors={sensors.data} initial={pf ?? undefined} onClose={() => setPf(undefined)} onSaved={() => { setPf(undefined); void pfma.reload(); toast('Failure mode saved.'); }} />}
    </>
  );
}
