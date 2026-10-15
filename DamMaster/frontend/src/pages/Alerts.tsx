import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api, fmtNum, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { Alert, AuditEntry, Interpretation, NotificationRow, ReadingPoint } from '../api/types';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import ReadingsChart from '../components/ReadingsChart';
import { ErrorBox, Field, LevelBadge, Modal, useToast } from '../components/ui';

const DISPOSITIONS = ['Confirmed', 'False alarm', 'Instrument fault', 'Resolved'];
interface Detail { alert: Alert; series: ReadingPoint[] | null; interpretations: Interpretation[]; history: AuditEntry[] }

function AlertDetail({ id, onClose, onChanged }: { id: string; onClose: () => void; onChanged: () => void }) {
  const { data, error, reload } = useApi<Detail>('/alerts/' + id); const { can } = useAuth(); const toast = useToast(); const nav = useNavigate();
  const notes = useApi<NotificationRow[]>(`/alerts/${id}/notifications`);
  const inspect = async () => { try { const r = await api.post<{ inspectionId: string }>(`/alerts/${id}/inspection`); onClose(); nav('/inspections/' + r.inspectionId); } catch (e) { setErr((e as Error).message); } };
  const [note, setNote] = useState(''); const [disp, setDisp] = useState('Confirmed'); const [err, setErr] = useState<string | null>(null);
  const act = async (kind: 'acknowledge' | 'close') => {
    try { await api.post(`/alerts/${id}/${kind}`, kind === 'close' ? { disposition: disp, note } : { note }); toast(kind === 'close' ? 'Alert closed.' : 'Alert acknowledged.'); setNote(''); setErr(null); void reload(); onChanged(); }
    catch (e) { setErr((e as Error).message); }
  };
  const a = data?.alert;
  return (
    <Modal title={a ? a.title : 'Alert'} onClose={onClose} wide>
      <ErrorBox error={error || err} />
      {a && <>
        <div className="row" style={{ marginBottom: 10 }}><LevelBadge level={a.level} /><span className="badge b-mute">{a.ruleType}</span><span className="badge b-info">{a.status}</span>
          <span className="muted small">{a.damName}{a.sensorCode && <> · <Link to={'/sensors/' + a.sensorId} onClick={onClose}>{a.sensorCode}</Link></>} · first {fmtTime(a.firstAt)} · last {fmtTime(a.lastAt)} · {a.occurrences}×</span></div>
        <p>{a.detail}</p>
        <div className="row" style={{ marginBottom: 10 }}>
          {a.status === 'Open' && a.ackDueAt && <span className={`badge ${new Date(a.ackDueAt) < new Date() ? 'b-3' : 'b-1'}`}>Acknowledge by {fmtTime(a.ackDueAt)}</span>}
          {a.escalationStep > 0 && <span className="badge b-2">Escalated — step {a.escalationStep}</span>}
          {a.inspectionId ? <Link to={'/inspections/' + a.inspectionId} onClick={onClose} className="btn sm">Open linked inspection</Link> : can('manageInspections') && <button className="btn sm" onClick={inspect}>Create special inspection</button>}
        </div>
        {data?.series && data.series.length > 0 && <ReadingsChart points={data.series} unit="" alert={a.ruleType === 'Threshold' ? a.limitValue : undefined} />}
        {data && data.interpretations.length > 0 && <><h4>What this pattern can mean (reference library)</h4>
          <div className="tablewrap"><table><thead><tr><th>Observation</th><th>Normal</th><th>Warning sign</th><th>Critical sign</th><th>Possible cause</th><th>Action</th></tr></thead>
            <tbody>{data.interpretations.map((i) => <tr key={i.id}><td><b>{i.observation}</b></td><td className="small">{i.normalPattern}</td><td className="small">{i.warningSign}</td><td className="small">{i.criticalSign}</td><td className="small">{i.possibleCause}</td><td className="small">{i.actionText}</td></tr>)}</tbody></table></div></>}
        {a.ackBy && <p className="small"><b>Acknowledged</b> by {a.ackBy}, {fmtTime(a.ackAt)}{a.ackNote && ` — ${a.ackNote}`}</p>}
        {a.closedBy && <p className="small"><b>Closed</b> as {a.disposition} by {a.closedBy}, {fmtTime(a.closedAt)} — {a.closeNote}</p>}
        {a.status !== 'Closed' && (can('acknowledgeAlerts') || can('closeAlerts')) && <div className="card" style={{ background: 'var(--soft)' }}>
          <Field label="Note"><textarea className="input" value={note} onChange={(e) => setNote(e.target.value)} placeholder="What was checked, what was found" /></Field>
          <div className="row">
            {a.status === 'Open' && can('acknowledgeAlerts') && <button className="btn" onClick={() => act('acknowledge')}>Acknowledge</button>}
            {can('closeAlerts') && <><select className="input" style={{ width: 'auto' }} value={disp} onChange={(e) => setDisp(e.target.value)} aria-label="Disposition">{DISPOSITIONS.map((d) => <option key={d}>{d}</option>)}</select>
              <button className="btn primary" onClick={() => act('close')}>Close alert</button></>}
          </div></div>}
        {notes.data && notes.data.length > 0 && <><h4>Notifications sent ({notes.data.length})</h4>
          <div className="tablewrap" style={{ maxHeight: 220 }}><table><thead><tr><th>Time (IST)</th><th>Channel</th><th>Recipient</th><th>Step</th><th>Status</th></tr></thead>
            <tbody>{notes.data.map((n) => <tr key={n.id}><td>{fmtTime(n.createdAt)}</td><td>{n.channel}</td><td>{n.recipientName}{n.channel !== 'InApp' && <span className="muted small mono"> · {n.recipient}</span>}</td><td>{n.escalationStep || 'initial'}</td><td>{n.status}</td></tr>)}</tbody></table></div></>}
        {data && data.history.length > 0 && <><h4>History</h4><ul className="small">{data.history.map((h) => <li key={h.id}>{fmtTime(h.at)} — {h.username}: {h.action}</li>)}</ul></>}
      </>}
    </Modal>
  );
}

export function AlertTable({ alerts, onChanged, showDam }: { alerts: Alert[]; onChanged: () => void; showDam?: boolean }) {
  const [open, setOpen] = useState<string | null>(null);
  return (
    <>
      <div className="tablewrap"><table>
        <thead><tr><th>Level</th><th>Alert</th>{showDam && <th>Dam</th>}<th>Rule</th><th>Value / limit</th><th>Last (IST)</th><th>Count</th><th>Status</th></tr></thead>
        <tbody>{alerts.map((a) => (
          <tr key={a.id} className="click" onClick={() => setOpen(a.id)}>
            <td><LevelBadge level={a.level} /></td><td><b>{a.title}</b></td>{showDam && <td>{a.damName}</td>}<td>{a.ruleType}</td>
            <td>{fmtNum(a.value, 3)} / {fmtNum(a.limitValue, 3)}</td><td>{fmtTime(a.lastAt)}</td><td>{a.occurrences}</td>
            <td><span className={`badge ${a.status === 'Closed' ? 'b-mute' : a.status === 'Open' ? 'b-3' : 'b-info'}`}>{a.status}</span>{a.disposition && <div className="muted small">{a.disposition}</div>}</td></tr>))}
          {alerts.length === 0 && <tr><td colSpan={8} className="empty">No alerts.</td></tr>}</tbody></table></div>
      {open && <AlertDetail id={open} onClose={() => setOpen(null)} onChanged={onChanged} />}
    </>
  );
}

export default function Alerts() {
  const [status, setStatus] = useState('Active'); const [minLevel, setMinLevel] = useState('');
  const { data, error, reload } = useApi<Alert[]>(`/alerts?status=${status}${minLevel ? '&minLevel=' + minLevel : ''}`);
  return (
    <>
      <div className="hero"><div><h1>Alerts</h1><p>Four levels: Alert (unusual), Alarm (unsafe trend or design limit), Emergency (failure possible — activate EAP). Alerts are only closed by a Dam Safety Engineer with a disposition.</p></div>
        <div className="row"><select className="input" style={{ width: 'auto' }} value={status} onChange={(e) => setStatus(e.target.value)} aria-label="Status"><option value="Active">Open + acknowledged</option><option value="Open">Open</option><option value="Acknowledged">Acknowledged</option><option value="Closed">Closed</option><option value="All">All</option></select>
          <select className="input" style={{ width: 'auto' }} value={minLevel} onChange={(e) => setMinLevel(e.target.value)} aria-label="Minimum level"><option value="">All levels</option><option value="1">Level 1+</option><option value="2">Level 2+</option><option value="3">Level 3</option></select></div></div>
      <ErrorBox error={error} />
      <div className="card"><AlertTable alerts={data ?? []} onChanged={() => void reload()} showDam /></div>
    </>
  );
}
