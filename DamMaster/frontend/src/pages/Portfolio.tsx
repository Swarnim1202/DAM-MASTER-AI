import { useApi } from '../api/hooks';
import { fmtTime } from '../api/client';
import { ErrorBox, Kpi } from '../components/ui';
import { BAND_CLASS, RATING_CLASS } from './DamPhase3Tabs';

interface DamRow { code: string; name: string; state?: string; hazardCode: string; dhi: number; band: string; risk: number; rating: string; maxOpenLevel: number; openAlerts: number; dataAsOf?: string | null }
interface Summary { instance: string; generatedAt: string; dams: DamRow[]; totals: { dams: number; openAlerts: number; level3: number; damsInAlarm: number } }
interface Pf { self: Summary | null; instances: { name: string; url: string; ok: boolean; error?: string | null; latencyMs: number; summary: Summary | null }[]; generatedAt: string }

export default function Portfolio() {
  const { data, error, reload } = useApi<Pf>('/federation/portfolio');
  const all = [...(data?.self ? [{ name: data.self.instance + ' (this instance)', ok: true, summary: data.self, error: null, latencyMs: 0 }] : []), ...(data?.instances ?? [])];
  const dams = all.flatMap((i) => (i.summary?.dams ?? []).map((d) => ({ ...d, instance: i.name }))).sort((a, b) => b.maxOpenLevel - a.maxOpenLevel || b.risk - a.risk);
  return (
    <>
      <div className="hero"><div><h1>Federated portfolio</h1><p>Each state or owner runs its own DAM MASTER instance and keeps its data; this view reads only their portfolio summaries (DHI, risk, open alerts) using a per-instance key.</p></div><button className="btn" onClick={() => void reload()}>Refresh</button></div>
      <ErrorBox error={error} />
      {data && <>
        <div className="grid g4"><Kpi label="Instances" value={`${all.filter((i) => i.ok).length} / ${all.length}`} sub="reachable" colour={all.every((i) => i.ok) ? '#12946a' : '#d98a1c'} />
          <Kpi label="Dams" value={dams.length} colour="#1f6fd1" /><Kpi label="Dams with Level 2+" value={dams.filter((d) => d.maxOpenLevel >= 2).length} colour="#d14545" /><Kpi label="Open alerts" value={dams.reduce((a, d) => a + d.openAlerts, 0)} colour="#6a55e0" /></div>
        <div className="card mt"><h2>Instances</h2><div className="tablewrap"><table><thead><tr><th>Instance</th><th>Status</th><th>Dams</th><th>Response</th><th>Generated (IST)</th></tr></thead>
          <tbody>{all.map((i) => <tr key={i.name}><td><b>{i.name}</b></td><td>{i.ok ? <span className="badge b-0">OK</span> : <span className="badge b-3">{i.error}</span>}</td><td>{i.summary?.dams.length ?? '—'}</td><td>{i.latencyMs ? `${i.latencyMs} ms` : '—'}</td><td>{fmtTime(i.summary?.generatedAt)}</td></tr>)}</tbody></table></div></div>
        <div className="card mt"><h2>All dams (highest alert level, then risk)</h2><div className="tablewrap"><table><thead><tr><th>Instance</th><th>Dam</th><th>Hazard</th><th>DHI</th><th>Risk</th><th>Open alerts</th><th>Data as of</th></tr></thead>
          <tbody>{dams.map((d) => <tr key={d.instance + d.code}><td className="small">{d.instance}</td><td><b>{d.name}</b><div className="muted small">{d.code} · {d.state}</div></td><td>{d.hazardCode}</td>
            <td><b>{d.dhi}</b> <span className={`badge ${BAND_CLASS[d.band]}`}>{d.band}</span></td><td>{d.risk} <span className={`badge ${RATING_CLASS[d.rating]}`}>{d.rating}</span></td><td>{d.openAlerts}{d.maxOpenLevel > 0 && <span className={`badge b-${d.maxOpenLevel}`} style={{ marginLeft: 6 }}>L{d.maxOpenLevel}</span>}</td><td>{fmtTime(d.dataAsOf)}</td></tr>)}</tbody></table></div></div></>}
    </>
  );
}
