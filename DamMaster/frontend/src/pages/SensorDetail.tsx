import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, fmtNum, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { ReadingPoint, Sensor, Threshold } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import ReadingsChart from '../components/ReadingsChart';
import { ErrorBox, Field, Modal, QualityBadge, num, useToast } from '../components/ui';
import { SensorForm } from './DamDetail';
import SensorAnalytics from './SensorAnalytics';

const METHODS = ['Design', 'Statistical', 'Historical', 'Rate of change', 'PFMA'];

export default function SensorDetail() {
  const { id } = useParams(); const { can, user } = useAuth(); const toast = useToast();
  const [days, setDays] = useState(90);
  const s = useApi<Sensor>('/sensors/' + id); const rd = useApi<ReadingPoint[]>(`/sensors/${id}/readings?days=${days}`); const th = useApi<Threshold[]>(`/sensors/${id}/thresholds`);
  const [edit, setEdit] = useState(false); const [newTh, setNewTh] = useState(false); const [err, setErr] = useState<string | null>(null);
  const [f, setF] = useState({ alert: '', alarm: '', emergency: '', rate: '', z: '2.5', persistence: '2', method: 'Design', basis: '' });
  const cur = th.data?.find((t) => t.isCurrent);
  const sensor = s.data;
  const saveTh = async () => {
    try {
      await api.post(`/sensors/${id}/thresholds`, { alertValue: num(f.alert), alarmValue: num(f.alarm), emergencyValue: num(f.emergency), rateLimitPerDay: num(f.rate), zLimit: num(f.z) ?? 2.5, persistence: num(f.persistence) ?? 2, method: f.method, basis: f.basis });
      setNewTh(false); setErr(null); toast('New threshold version saved — pending approval.'); void th.reload();
    } catch (e) { setErr((e as Error).message); }
  };
  const approve = async (t: Threshold) => { try { await api.post(`/sensors/thresholds/${t.id}/approve`); toast('Threshold approved.'); void th.reload(); } catch (e) { toast((e as Error).message); } };
  if (!sensor) return <ErrorBox error={s.error} />;
  const pts = rd.data ?? []; const valid = pts.filter((p) => p.q !== 'B'); const last = valid[valid.length - 1];
  return (
    <>
      <div className="hero"><div><h1>{sensor.code}</h1><p>{sensor.parameterCode} · {sensor.location} · unit {sensor.engUnit} · danger when {sensor.direction.toLowerCase()} · every {sensor.readingIntervalMin} min · <Link to={'/dams/' + sensor.damId}>back to dam</Link></p></div>
        <div className="row"><select className="input" style={{ width: 'auto' }} value={days} onChange={(e) => setDays(Number(e.target.value))} aria-label="Period">{[14, 30, 90, 365, 0].map((d) => <option key={d} value={d}>{d ? `Last ${d} days` : 'All data'}</option>)}</select>
          {can('manageSensors') && <button className="btn" onClick={() => setEdit(true)}>Edit sensor</button>}</div></div>
      <div className="grid g21">
        <div className="card"><h2>Readings <span className="muted small">{pts[0]?.q === 'D' ? `${pts.length} daily means (long period — aggregated)` : `${pts.length} readings · B (bad) hidden, S (suspect) marked`}</span></h2>
          {pts.length ? <ReadingsChart points={pts} unit={sensor.engUnit} alert={cur?.alertValue} alarm={cur?.alarmValue} emergency={cur?.emergencyValue} /> : <div className="empty">No readings yet.</div>}</div>
        <div className="card"><h2>Current status</h2><div className="kv">
          <span>Latest valid</span><b>{last ? `${fmtNum(last.v)} ${sensor.engUnit}` : '—'}</b><span>Time (IST)</span><b>{fmtTime(last?.t)}</b>
          <span>Expected range</span><b>{sensor.expectedMin ?? '—'} – {sensor.expectedMax ?? '—'}</b><span>Calibration due</span><b>{sensor.calibrationDue?.slice(0, 10) ?? '—'}</b>
          <span>Alert / Alarm / Emergency</span><b>{cur ? `${cur.alertValue ?? '—'} / ${cur.alarmValue ?? '—'} / ${cur.emergencyValue ?? '—'}` : 'Not set'}</b>
          <span>Rate limit</span><b>{cur?.rateLimitPerDay != null ? `${cur.rateLimitPerDay} ${sensor.engUnit}/day` : '—'}</b><span>Statistical limit</span><b>{cur ? `|z| > ${cur.zLimit} for ${cur.persistence} readings` : '—'}</b>
          <span>Approval</span><b>{cur ? (cur.approvedBy ? `Approved by ${cur.approvedBy}` : <span className="badge b-1">Pending approval</span>) : '—'}</b></div></div>
      </div>
      <div className="card mt"><h2>Threshold versions {can('manageSensors') && <button className="btn primary sm" onClick={() => setNewTh(true)}>+ New version</button>}</h2>
        <p className="muted small" style={{ marginTop: 0 }}>Every change creates a new version with its basis. Approval must be by a Dam Safety Engineer or Approver other than the author.</p>
        <div className="tablewrap"><table><thead><tr><th>Created</th><th>By</th><th>Alert / Alarm / Emergency</th><th>Rate/day</th><th>z · persistence</th><th>Method</th><th>Basis</th><th>Approval</th><th></th></tr></thead>
          <tbody>{th.data?.map((t) => (
            <tr key={t.id} style={t.isCurrent ? { background: 'var(--bbg)' } : undefined}><td>{fmtTime(t.createdAt)}{t.isCurrent && <div><span className="badge b-info">current</span></div>}</td><td>{t.createdBy}</td>
              <td>{t.alertValue ?? '—'} / {t.alarmValue ?? '—'} / {t.emergencyValue ?? '—'}</td><td>{t.rateLimitPerDay ?? '—'}</td><td>{t.zLimit} · {t.persistence}</td><td>{t.method}</td><td className="small">{t.basis}</td>
              <td>{t.approvedBy ? `${t.approvedBy}, ${fmtTime(t.approvedAt)}` : '—'}</td>
              <td>{!t.approvedBy && t.isCurrent && can('approveThresholds') && t.createdBy !== user?.fullName && <button className="btn sm green" onClick={() => approve(t)}>Approve</button>}</td></tr>))}
            {th.data?.length === 0 && <tr><td colSpan={9} className="empty">No thresholds — alerts use only statistical checks until thresholds are set.</td></tr>}</tbody></table></div></div>
      <SensorAnalytics sensorId={sensor.id} unit={sensor.engUnit} parameterCode={sensor.parameterCode} />
      <div className="card mt"><h2>Latest readings</h2><div className="tablewrap" style={{ maxHeight: 320 }}><table><thead><tr><th>Observed (IST)</th><th>Value</th><th>Flag</th><th>QC rule</th></tr></thead>
        <tbody>{pts.slice(-60).reverse().map((p) => <tr key={p.t}><td>{fmtTime(p.t)}</td><td>{fmtNum(p.v, 3)} {sensor.engUnit}</td><td><QualityBadge q={p.q} /></td><td className="small">{p.rule}</td></tr>)}</tbody></table></div></div>
      {edit && <SensorForm damId={sensor.damId} initial={sensor} onClose={() => setEdit(false)} onSaved={() => { setEdit(false); void s.reload(); toast('Sensor updated.'); }} />}
      {newTh && <Modal title={`New threshold version — ${sensor.code}`} onClose={() => setNewTh(false)} footer={<><button className="btn" onClick={() => setNewTh(false)}>Cancel</button><button className="btn primary" onClick={saveTh}>Save version</button></>}>
        <ErrorBox error={err} />
        <div className="form">
          <Field label={`Level 1 Alert (${sensor.engUnit})`}><input className="input" type="number" step="any" value={f.alert} onChange={(e) => setF({ ...f, alert: e.target.value })} /></Field>
          <Field label="Level 2 Alarm"><input className="input" type="number" step="any" value={f.alarm} onChange={(e) => setF({ ...f, alarm: e.target.value })} /></Field>
          <Field label="Level 3 Emergency"><input className="input" type="number" step="any" value={f.emergency} onChange={(e) => setF({ ...f, emergency: e.target.value })} /></Field>
          <Field label={`Rate limit (${sensor.engUnit}/day)`}><input className="input" type="number" step="any" value={f.rate} onChange={(e) => setF({ ...f, rate: e.target.value })} /></Field>
          <Field label="Statistical |z| limit"><input className="input" type="number" step="0.1" value={f.z} onChange={(e) => setF({ ...f, z: e.target.value })} /></Field>
          <Field label="Persistence (consecutive readings)"><input className="input" type="number" min={1} max={10} value={f.persistence} onChange={(e) => setF({ ...f, persistence: e.target.value })} /></Field>
          <Field label="Method"><select className="input" value={f.method} onChange={(e) => setF({ ...f, method: e.target.value })}>{METHODS.map((m) => <option key={m}>{m}</option>)}</select></Field>
        </div>
        <Field label="Basis (design document, statistics or PFMA reference)"><textarea className="input" value={f.basis} onChange={(e) => setF({ ...f, basis: e.target.value })} /></Field>
        <p className="note">Level 3 always needs two consecutive readings beyond the emergency value (confirmation) before it is raised.</p>
      </Modal>}
    </>
  );
}
