import { api, fmtNum, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { SystemStatus as S } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBox, Kpi, useToast } from '../components/ui';

export default function SystemStatus() {
  const { data, error, reload } = useApi<S>('/system/status'); const { can } = useAuth(); const toast = useToast();
  const run = async () => { try { const r = await api.post<Record<string, string>>('/system/run-jobs'); toast(Object.entries(r).map(([k, v]) => `${k}: ${v}`).join(' · ')); void reload(); } catch (e) { toast((e as Error).message); } };
  const m = data?.mqtt;
  return (
    <>
      <div className="hero"><div><h1>System status</h1><p>Live IoT connection, background jobs and storage. Jobs run every minute (escalation, dispatch, gateway health) and daily (DHI snapshot, HST refit check).</p></div>
        <div className="row"><button className="btn" onClick={() => void reload()}>Refresh</button>{can('runJobs') && <button className="btn primary" onClick={run}>Run all jobs now</button>}</div></div>
      <ErrorBox error={error} />
      {data && m && <>
        <div className="grid g4">
          <Kpi label="MQTT" value={!m.enabled ? 'Disabled' : m.connected ? 'Connected' : 'Disconnected'} sub={m.broker ?? 'Mqtt:Enabled = false in appsettings'} colour={!m.enabled ? '#8193a5' : m.connected ? '#12946a' : '#d14545'} />
          <Kpi label="Messages received" value={fmtNum(m.received, 0)} sub={`${m.duplicateMessages} duplicate · ${m.failed} failed`} colour="#1f6fd1" />
          <Kpi label="Readings stored" value={fmtNum(data.storage.readings, 0)} sub={`${fmtNum(data.storage.dailyAggregates, 0)} daily aggregates`} colour="#6a55e0" />
          <Kpi label="Database size" value={`${fmtNum(data.storage.databaseSizeMb, 1)} MB`} sub={data.storage.partitioned ? `readings partitioned (${data.storage.partitions.length})` : 'readings not partitioned'} colour="#d98a1c" />
        </div>
        <div className="grid g2 mt">
          <div className="card"><h2>MQTT ingestion</h2><div className="kv"><span>Topics</span><b className="mono small">{m.topics.join(', ') || '—'}</b><span>Connected since</span><b>{fmtTime(m.connectedSince)}</b><span>Last message</span><b>{fmtTime(m.lastMessageAt)}</b><span>Last error</span><b className="small">{m.lastError ?? '—'}</b></div>
            <p className="muted small">Persistent session (QoS 1): the broker keeps messages while the API restarts. Gateways should buffer on the logger while their link is down.</p></div>
          <div className="card"><h2>Background jobs</h2><div className="tablewrap"><table><thead><tr><th>Job</th><th>Last run (IST)</th><th>Result</th></tr></thead>
            <tbody>{data.jobs.map((j) => <tr key={j.job}><td><b>{j.job}</b></td><td>{fmtTime(j.lastRunAt)}</td><td><span className={`badge ${j.lastOk ? 'b-0' : 'b-3'}`}>{j.lastOk ? 'OK' : 'Error'}</span> <span className="small">{j.lastResult}</span></td></tr>)}</tbody></table></div></div>
        </div>
        {data.storage.partitioned && <div className="card mt"><h2>Readings partitions</h2><div className="tablewrap"><table><thead><tr><th>Partition</th><th>Rows (estimate)</th></tr></thead><tbody>{data.storage.partitions.map((p) => <tr key={p.name}><td className="mono">{p.name}</td><td>{fmtNum(p.rows, 0)}</td></tr>)}</tbody></table></div>
          <p className="muted small">Add next year's partition every December: <span className="mono">CALL dm_add_reading_partition(YYYY);</span></p></div>}
      </>}
    </>
  );
}
