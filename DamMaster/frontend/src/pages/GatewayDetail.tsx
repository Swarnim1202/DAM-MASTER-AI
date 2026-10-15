import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Line } from 'react-chartjs-2';
import { api, fmtNum, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { Alert, GatewayHealth, GatewayMessageRow, GatewayStatusRow, MqttTopics, ReconciliationRow } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import '../components/AnalyticsCharts';
import { ErrorBox, Field, LevelBadge, Modal, useToast } from '../components/ui';
import { STATE_CLASS, TopicsBox, batteryClass, signalText } from './Gateways';

interface Detail { gateway: GatewayHealth; messages: GatewayMessageRow[]; status: GatewayStatusRow[]; alerts: Alert[]; mqtt: MqttTopics }

export default function GatewayDetail() {
  const { id } = useParams(); const { can } = useAuth(); const toast = useToast();
  const { data, error, reload } = useApi<Detail>(`/gateways/${id}`); const [days, setDays] = useState(7);
  const rec = useApi<{ rows: ReconciliationRow[] }>(`/gateways/${id}/reconciliation?days=${days}`);
  const [edit, setEdit] = useState<{ expectedIntervalMin: number; isActive: boolean } | null>(null); const [key, setKey] = useState<string | null>(null);
  if (!data) return <ErrorBox error={error} />;
  const g = data.gateway;
  const save = async () => { try { await api.put(`/gateways/${id}`, edit); setEdit(null); toast('Settings saved.'); void reload(); } catch (e) { toast((e as Error).message); } };
  const rotate = async () => { try { setKey((await api.post<{ apiKey: string }>(`/gateways/${id}/rotate-key`)).apiKey); } catch (e) { toast((e as Error).message); } };
  return (
    <>
      <div className="hero"><div><h1>{g.code}</h1><p><span className={`badge ${STATE_CLASS[g.state]}`}>{g.state}</span> · <Link to={'/dams/' + g.damId}>{g.damName}</Link> · {g.transport ?? 'no data yet'} · expected every {g.expectedIntervalMin} min · last message {fmtTime(g.lastMessageAt)}</p></div>
        {can('manageGateways') && <div className="row"><button className="btn" onClick={() => setEdit({ expectedIntervalMin: g.expectedIntervalMin, isActive: g.isActive })}>Settings</button><button className="btn" onClick={rotate}>Rotate HTTP key</button></div>}</div>
      <div className="grid g4">
        <div className="card"><div className="kv"><span>Battery</span><b><span className={`badge ${batteryClass(g.batteryV)}`}>{g.batteryV != null ? `${fmtNum(g.batteryV, 2)} V` : '—'}</span></b><span>Signal</span><b>{signalText(g.signalDbm)}</b><span>Firmware</span><b>{g.firmware ?? '—'}</b></div></div>
        <div className="card"><div className="kv"><span>Buffered on logger</span><b>{g.bufferedCount ?? '—'}</b><span>Last status</span><b>{fmtTime(g.lastStatusAt)}</b></div></div>
        <div className="card"><div className="kv"><span>Messages 24 h</span><b>{g.messages24h}</b><span>Readings 24 h</span><b>{g.readings24h}</b><span>Late / rejected</span><b>{g.late24h} / {g.rejected24h}</b></div></div>
        <div className="card"><div className="kv"><span>Average latency</span><b>{g.avgLatencyS != null ? `${fmtNum(g.avgLatencyS / 60, 1)} min` : '—'}</b><span>Open alerts</span><b>{g.openAlerts}</b></div></div>
      </div>
      <div className="grid g2 mt">
        <div className="card"><h2>Battery and signal (last 7 days)</h2>{data.status.length ? <div className="chartbox" style={{ height: 240 }}><Line data={{ labels: data.status.map((s) => fmtTime(s.at)), datasets: [
          { label: 'Battery (V)', data: data.status.map((s) => s.batteryV ?? null), borderColor: '#12946a', yAxisID: 'y', pointRadius: 2, spanGaps: true },
          { label: 'Signal (dBm)', data: data.status.map((s) => s.signalDbm ?? null), borderColor: '#6a55e0', yAxisID: 'y1', pointRadius: 2, spanGaps: true }] }}
          options={{ responsive: true, maintainAspectRatio: false, animation: false, plugins: { legend: { position: 'bottom', labels: { boxWidth: 10 } } }, scales: { y: { position: 'left', title: { display: true, text: 'V' } }, y1: { position: 'right', grid: { display: false }, title: { display: true, text: 'dBm' } } } }} /></div> : <p className="muted">No status messages yet. Gateways should publish to the status topic every hour.</p>}</div>
        <div className="card"><h2>Connection details</h2><TopicsBox t={data.mqtt} /></div>
      </div>
      <div className="card mt"><h2>Reconciliation — buffered and missing data <select className="input" style={{ width: 'auto' }} value={days} onChange={(e) => setDays(Number(e.target.value))} aria-label="Window">{[1, 7, 30].map((d) => <option key={d} value={d}>Last {d} day{d > 1 ? 's' : ''}</option>)}</select></h2>
        <p className="muted small" style={{ marginTop: 0 }}>Back-filled = readings that arrived more than two intervals after they were measured (sent from the logger's buffer after an outage). Gaps = periods still without data.</p>
        <div className="tablewrap"><table><thead><tr><th>Sensor</th><th>Expected</th><th>Received</th><th>Back-filled</th><th>Gaps open</th><th>Longest gap</th><th>Largest gaps (IST)</th></tr></thead>
          <tbody>{rec.data?.rows.map((r) => <tr key={r.sensorId}><td className="mono"><Link to={'/sensors/' + r.sensorId}>{r.code}</Link></td><td>{r.expected}</td><td>{r.received}</td><td>{r.backfilled}</td><td>{r.gaps ? <span className="badge b-1">{r.gaps}</span> : 0}</td><td>{r.longestGapHours} h</td>
            <td className="small">{r.topGaps.slice(0, 2).map((x, i) => <div key={i}>{fmtTime(x.from)} → {fmtTime(x.to)} ({x.hours} h)</div>)}</td></tr>)}</tbody></table></div></div>
      <div className="grid g2 mt">
        <div className="card"><h2>Message log</h2><div className="tablewrap" style={{ maxHeight: 360 }}><table><thead><tr><th>Received (IST)</th><th>Message</th><th>Via</th><th>Accepted</th><th>Rej / Q / Dup</th><th>Late</th><th>Max latency</th></tr></thead>
          <tbody>{data.messages.map((m) => <tr key={m.id}><td>{fmtTime(m.receivedAt)}</td><td className="mono small">{m.messageId}</td><td>{m.transport}</td><td>{m.accepted}/{m.readings}</td><td>{m.rejected} / {m.quarantined} / {m.duplicates}</td><td>{m.late}</td><td>{m.maxLatencyS != null ? `${fmtNum(m.maxLatencyS / 60, 0)} min` : '—'}{m.error && <div className="small" style={{ color: 'var(--rfg)' }}>{m.error}</div>}</td></tr>)}</tbody></table></div></div>
        <div className="card"><h2>Gateway alerts</h2><div className="tablewrap"><table><tbody>{data.alerts.map((a) => <tr key={a.id}><td><LevelBadge level={a.level} /></td><td><b>{a.title}</b><div className="muted small">{fmtTime(a.firstAt)}{a.status === 'Closed' ? ` · resolved: ${a.closeNote}` : ''}</div></td><td>{a.status}</td></tr>)}
          {data.alerts.length === 0 && <tr><td className="empty">No gateway alerts.</td></tr>}</tbody></table></div></div>
      </div>
      {edit && <Modal title="Gateway settings" onClose={() => setEdit(null)} footer={<><button className="btn" onClick={() => setEdit(null)}>Cancel</button><button className="btn primary" onClick={save}>Save</button></>}>
        <Field label="Expected message every (minutes)"><input className="input" type="number" min={1} max={1440} value={edit.expectedIntervalMin} onChange={(e) => setEdit({ ...edit, expectedIntervalMin: Number(e.target.value) || 60 })} /></Field>
        <Field label="Status"><select className="input" value={edit.isActive ? 'Active' : 'Disabled'} onChange={(e) => setEdit({ ...edit, isActive: e.target.value === 'Active' })}><option>Active</option><option>Disabled</option></select></Field>
        <p className="muted small">A disabled gateway's messages are refused and no offline alerts are raised.</p></Modal>}
      {key && <Modal title="New HTTP API key" onClose={() => setKey(null)} footer={<button className="btn primary" onClick={() => setKey(null)}>I have stored the key</button>}><p className="note">The old key no longer works. Shown only once:</p><p className="mono" style={{ wordBreak: 'break-all' }}><b>{key}</b></p></Modal>}
    </>
  );
}
