import { Link, useNavigate } from 'react-router-dom';
import { fmtNum, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { DamSummary } from '../api/types';
import { ErrorBox, Kpi, LevelBadge } from '../components/ui';
import { BAND_CLASS, RATING_CLASS } from './DamPhase3Tabs';

interface Portfolio { dams: DamSummary[]; totals: { dams: number; sensors: number; readings: number; openAlerts: number; level1: number; level2: number; level3: number; damsInAlarm: number; highHazard: number; openFindings: number; highFindings: number; overdueInspections: number; averageDhi?: number | null } }

export default function Dashboard() {
  const { data, error } = useApi<Portfolio>('/dashboard/portfolio');
  const nav = useNavigate();
  const t = data?.totals;
  return (
    <>
      <div className="hero"><div><h1>Portfolio dashboard</h1><p>Every dam, its instruments and its open alerts. Dams with the highest alert level are listed first.</p></div></div>
      <ErrorBox error={error} />
      <div className="grid g5">
        <Kpi label="Dams" value={t?.dams ?? '—'} sub={`${t?.highHazard ?? 0} high hazard`} colour="#8193a5" />
        <Kpi label="Sensors / readings" value={t?.sensors ?? '—'} sub={`${fmtNum(t?.readings, 0)} readings`} />
        <Kpi label="Open alerts" value={t?.openAlerts ?? '—'} sub={`L1 ${t?.level1 ?? 0} · L2 ${t?.level2 ?? 0} · L3 ${t?.level3 ?? 0}`} colour="#d98a1c" />
        <Kpi label="Dams at Alarm or worse" value={t?.damsInAlarm ?? '—'} sub="Level 2 or 3 open" colour="#d14545" />
        <Kpi label="Emergency (Level 3)" value={t?.level3 ?? '—'} sub="EAP activation required" colour="#d14545" />
      </div>
      <div className="grid g3 mt">
        <Kpi label="Open inspection findings" value={<Link to="/inspections">{t?.openFindings ?? '—'}</Link>} sub={`${t?.highFindings ?? 0} high or critical`} colour="#e06b1a" />
        <Kpi label="Overdue inspections" value={t?.overdueInspections ?? '—'} sub="Planned / in progress past their date" colour="#6a55e0" />
        <Kpi label="Average Dam Health Index" value={t?.averageDhi ?? '—'} sub={<Link to="/inspections">Monsoon inspection compliance →</Link>} colour="#12946a" />
      </div>
      <div className="card mt">
        <h2>Dams <Link to="/dams" className="small">Manage dams →</Link></h2>
        {data && data.dams.length === 0 ? <div className="empty">No dams yet. <Link to="/import">Import the Excel template</Link> or <Link to="/dams">register a dam</Link>.</div> :
        <div className="tablewrap"><table>
          <thead><tr><th>Dam</th><th>DHI</th><th>Risk</th><th>Type</th><th>Hazard</th><th>Life phase</th><th>Status</th><th>Open alerts</th><th>Sensors</th><th>Reservoir level</th><th>Data as of (IST)</th><th>PFMA</th></tr></thead>
          <tbody>{data?.dams.map((d) => (
            <tr key={d.id} className="click" onClick={() => nav('/dams/' + d.id)}>
              <td><b>{d.name}</b><div className="muted small">{d.code} · {d.state}</div></td><td><b>{d.dhi ?? '—'}</b> {d.dhiBand && <span className={`badge ${BAND_CLASS[d.dhiBand]}`}>{d.dhiBand}</span>}</td><td>{d.risk ?? '—'} {d.rating && <span className={`badge ${RATING_CLASS[d.rating]}`}>{d.rating}</span>}</td><td>{d.damTypeCode}</td><td>{d.hazardCode}</td><td>{d.lifePhase}</td>
              <td><LevelBadge level={d.maxOpenLevel} /></td><td>{d.openAlerts}</td><td>{d.sensors}</td>
              <td>{d.reservoirLevel != null ? `${fmtNum(d.reservoirLevel)} m` : '—'}{d.frlM != null && d.reservoirLevel != null && <div className="muted small">{fmtNum(Number(d.frlM) - d.reservoirLevel)} m below FRL</div>}</td>
              <td>{fmtTime(d.dataAsOf)}</td><td>{d.activePfma}</td>
            </tr>))}</tbody>
        </table></div>}
      </div>
    </>
  );
}
