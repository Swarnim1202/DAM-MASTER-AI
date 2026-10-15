import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, fmtNum, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { Dam, GatewayHealth, MqttTopics } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBox, Field, Kpi, Modal, useToast } from '../components/ui';

export const STATE_CLASS: Record<string, string> = { Online: 'b-0', Late: 'b-1', Offline: 'b-3', Disabled: 'b-mute', 'Never connected': 'b-info' };
export const batteryClass = (v?: number | null) => (v == null ? 'b-mute' : v < 11.5 ? 'b-3' : v < 12 ? 'b-1' : 'b-0');
export const signalText = (d?: number | null) => (d == null ? '—' : `${d} dBm ${d >= -70 ? '(good)' : d >= -85 ? '(fair)' : '(weak)'}`);

export function TopicsBox({ t, apiKey }: { t: MqttTopics; apiKey?: string }) {
  return <div className="card" style={{ background: 'var(--soft)' }}><div className="kv">
    <span>Telemetry (publish, QoS 1)</span><b className="mono">{t.telemetryTopic}</b><span>Status (publish)</span><b className="mono">{t.statusTopic}</b><span>Acknowledgements (subscribe)</span><b className="mono">{t.ackTopic}</b>
    {apiKey && <><span>HTTP API key (shown once)</span><b className="mono" style={{ wordBreak: 'break-all' }}>{apiKey}</b></>}</div>
    <p className="muted small">MQTT gateways authenticate to the broker with their own credentials; the broker's ACL must allow each gateway to publish only under its own topic. The HTTP key is for gateways that post to /api/telemetry instead.</p></div>;
}

export default function Gateways() {
  const { data, error, reload } = useApi<GatewayHealth[]>('/gateways'); const dams = useApi<Dam[]>('/dams'); const { can } = useAuth(); const nav = useNavigate(); const toast = useToast();
  const [add, setAdd] = useState(false); const [f, setF] = useState({ code: '', damId: '', expectedIntervalMin: 60 }); const [err, setErr] = useState<string | null>(null);
  const [created, setCreated] = useState<{ code: string; apiKey: string; mqtt: MqttTopics } | null>(null);
  const create = async () => { try { const r = await api.post<{ code: string; apiKey: string; mqtt: MqttTopics }>('/gateways', { ...f, damId: f.damId || dams.data?.[0]?.id }); setAdd(false); setCreated(r); toast('Gateway registered.'); void reload(); } catch (e) { setErr((e as Error).message); } };
  const count = (s: string) => data?.filter((g) => g.state === s).length ?? 0;
  return (
    <>
      <div className="hero"><div><h1>Gateways</h1><p>Every data logger / IoT gateway: connection state, battery, signal, message flow and late (buffered) data. A gateway silent for more than 3 expected intervals raises a Level 1 alert that closes itself when data returns.</p></div>
        {can('manageGateways') && <button className="btn primary" onClick={() => { setAdd(true); setErr(null); }}>+ Register gateway</button>}</div>
      <ErrorBox error={error} />
      <div className="grid g4"><Kpi label="Online" value={count('Online')} colour="#12946a" /><Kpi label="Late" value={count('Late')} sub="1.5–3 intervals without data" colour="#d98a1c" /><Kpi label="Offline" value={count('Offline')} sub="> 3 intervals — alert raised" colour="#d14545" /><Kpi label="Readings (24 h)" value={fmtNum(data?.reduce((a, g) => a + g.readings24h, 0), 0)} sub={`${data?.reduce((a, g) => a + g.late24h, 0) ?? 0} arrived late`} colour="#1f6fd1" /></div>
      <div className="card mt"><div className="tablewrap"><table><thead><tr><th>Gateway</th><th>Dam</th><th>State</th><th>Last message (IST)</th><th>Every</th><th>Battery</th><th>Signal</th><th>Buffered</th><th>Msgs / readings 24 h</th><th>Late / rejected</th><th>Avg latency</th><th>Alerts</th></tr></thead>
        <tbody>{data?.map((g) => <tr key={g.id} className="click" onClick={() => nav('/gateways/' + g.id)}>
          <td className="mono"><b>{g.code}</b><div className="muted small">{g.transport ?? '—'} · fw {g.firmware ?? '—'}</div></td><td>{g.damName}</td><td><span className={`badge ${STATE_CLASS[g.state] ?? 'b-mute'}`}>{g.state}</span></td>
          <td>{fmtTime(g.lastMessageAt ?? g.lastSeenAt)}</td><td>{g.expectedIntervalMin} min</td><td><span className={`badge ${batteryClass(g.batteryV)}`}>{g.batteryV != null ? `${fmtNum(g.batteryV, 1)} V` : '—'}</span></td>
          <td className="small">{signalText(g.signalDbm)}</td><td>{g.bufferedCount ?? '—'}</td><td>{g.messages24h} / {g.readings24h}</td><td>{g.late24h} / {g.rejected24h}</td><td>{g.avgLatencyS != null ? `${fmtNum(g.avgLatencyS / 60, 1)} min` : '—'}</td><td>{g.openAlerts}</td></tr>)}
          {data?.length === 0 && <tr><td colSpan={12} className="empty">No gateways registered.</td></tr>}</tbody></table></div></div>
      {add && <Modal title="Register gateway" onClose={() => setAdd(false)} footer={<><button className="btn" onClick={() => setAdd(false)}>Cancel</button><button className="btn primary" onClick={create}>Register</button></>}>
        <ErrorBox error={err} /><div className="form two">
          <Field label="Gateway code"><input className="input" value={f.code} onChange={(e) => setF({ ...f, code: e.target.value })} placeholder="GW-DM001-02" /></Field>
          <Field label="Dam"><select className="input" value={f.damId} onChange={(e) => setF({ ...f, damId: e.target.value })}>{dams.data?.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}</select></Field>
          <Field label="Expected message every (minutes)"><input className="input" type="number" min={1} max={1440} value={f.expectedIntervalMin} onChange={(e) => setF({ ...f, expectedIntervalMin: Number(e.target.value) || 60 })} /></Field></div></Modal>}
      {created && <Modal title={`Gateway ${created.code} registered`} onClose={() => setCreated(null)} footer={<button className="btn primary" onClick={() => setCreated(null)}>I have stored the key</button>}>
        <p className="note">Copy the API key now — it cannot be shown again (it can be rotated later).</p><TopicsBox t={created.mqtt} apiKey={created.apiKey} /></Modal>}
    </>
  );
}
