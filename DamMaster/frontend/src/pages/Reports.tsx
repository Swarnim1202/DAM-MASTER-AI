import { useState } from 'react';
import { fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { AvailabilityReport, Dam } from '../api/types';
import { ErrorBox, Kpi } from '../components/ui';

export default function Reports() {
  const dams = useApi<Dam[]>('/dams'); const [damId, setDamId] = useState(''); const [days, setDays] = useState(30); const [now, setNow] = useState(false);
  const id = damId || dams.data?.[0]?.id || '';
  const rep = useApi<AvailabilityReport>(id ? `/reports/availability?damId=${id}&days=${days}&relativeToNow=${now}` : null);
  const r = rep.data;
  const csv = () => {
    if (!r) return;
    const head = ['sensor', 'parameter', 'interval_min', 'expected', 'received', 'valid', 'suspect', 'bad', 'availability_pct', 'valid_pct', 'longest_gap_h', 'last_reading_utc'];
    const lines = [head.join(','), ...r.rows.map((x) => [x.code, x.parameterCode, x.intervalMin, x.expected, x.received, x.valid, x.suspect, x.bad, x.availabilityPct, x.validPct, x.longestGapHours, x.lastReading ?? ''].join(','))];
    const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([lines.join('\n')], { type: 'text/csv' })); a.download = `availability_${r.damName.replace(/\W+/g, '_')}_${days}d.csv`; a.click(); URL.revokeObjectURL(a.href);
  };
  const cls = (p: number) => (p >= 95 ? 'b-0' : p >= 80 ? 'b-1' : 'b-3');
  return (
    <>
      <div className="hero"><div><h1>Data availability report</h1><p>Expected vs received readings per sensor, valid share, and the longest gap — the evidence behind the instrumentation score of the DHI.</p></div>
        <div className="row"><select className="input" style={{ width: 'auto' }} value={id} onChange={(e) => setDamId(e.target.value)} aria-label="Dam">{dams.data?.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}</select>
          <select className="input" style={{ width: 'auto' }} value={days} onChange={(e) => setDays(Number(e.target.value))} aria-label="Window">{[7, 30, 90, 365].map((d) => <option key={d} value={d}>Last {d} days</option>)}</select>
          <select className="input" style={{ width: 'auto' }} value={now ? 'now' : 'data'} onChange={(e) => setNow(e.target.value === 'now')} aria-label="Window end"><option value="data">ending at latest reading</option><option value="now">ending now</option></select>
          <button className="btn" onClick={csv} disabled={!r}>⤓ CSV</button></div></div>
      <ErrorBox error={rep.error} />
      {r && <>
        <div className="grid g4"><Kpi label="Dam" value={<span style={{ fontSize: 18 }}>{r.damName}</span>} sub={`${fmtTime(r.windowStart)} – ${fmtTime(r.windowEnd)}`} colour="#8193a5" />
          <Kpi label="Availability" value={`${r.availabilityPct}%`} sub="received ÷ expected" colour={r.availabilityPct >= 95 ? '#12946a' : '#d98a1c'} />
          <Kpi label="Valid data" value={`${r.validPct}%`} sub="G + S readings ÷ expected" colour={r.validPct >= 95 ? '#12946a' : '#d98a1c'} />
          <Kpi label="Sensors" value={r.rows.length} sub={`${r.rows.filter((x) => x.availabilityPct < 80).length} below 80%`} colour="#6a55e0" /></div>
        <div className="card mt"><div className="tablewrap"><table><thead><tr><th>Sensor</th><th>Interval</th><th>Expected</th><th>Received</th><th>Valid / S / B</th><th>Availability</th><th>Valid</th><th>Longest gap</th><th>Last reading (IST)</th></tr></thead>
          <tbody>{r.rows.map((x) => <tr key={x.sensorId}><td className="mono"><b>{x.code}</b> <span className="muted">{x.parameterCode}</span></td><td>{x.intervalMin / 60} h</td><td>{x.expected}</td><td>{x.received}</td><td>{x.valid} / {x.suspect} / {x.bad}</td>
            <td><span className={`badge ${cls(x.availabilityPct)}`}>{x.availabilityPct}%</span></td><td><span className={`badge ${cls(x.validPct)}`}>{x.validPct}%</span></td><td>{x.longestGapHours} h</td><td>{fmtTime(x.lastReading)}</td></tr>)}</tbody></table></div></div></>}
    </>
  );
}
