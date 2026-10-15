import { useApi } from '../api/hooks';
import { ErrorBox, Kpi } from '../components/ui';

interface T { name: string; formula: string; inputs: string; expected: number; got: number; tol: number; pass: boolean }
const n = (v: number) => (Math.abs(v) >= 1000 ? v.toLocaleString('en-IN', { maximumFractionDigits: 2 }) : Number(v.toPrecision(7)).toString());

export default function DesignValidation() {
  const { data, error, reload } = useApi<{ passed: number; total: number; tests: T[] }>('/design/validation');
  return (
    <>
      <div className="hero"><div><h1>Design engine validation</h1><p>The server's design calculations are run against independent reference values — hand calculations and a separate Python implementation (the same cases as the prototype calculation handbook). Run after every software update.</p></div><button className="btn primary" onClick={() => void reload()}>▶ Run again</button></div>
      <ErrorBox error={error} />
      {data && <><div className="grid g3"><Kpi label="Tests passed" value={`${data.passed} / ${data.total}`} colour={data.passed === data.total ? '#12946a' : '#d14545'} /><Kpi label="Engine" value="C# (server)" sub="DesignMath + DesignCalcs" colour="#1f6fd1" /><Kpi label="Reference" value="Handbook RCPL-DMAI-KB-001" sub="worked examples and expected values" colour="#6a55e0" /></div>
        <div className="card mt"><div className="tablewrap"><table><thead><tr><th>#</th><th>Test</th><th>Topic</th><th>Inputs</th><th>Expected</th><th>Computed</th><th>Tolerance</th><th>Result</th></tr></thead>
          <tbody>{data.tests.map((t, i) => <tr key={i}><td>{i + 1}</td><td>{t.name}</td><td className="small">{t.formula}</td><td className="small muted">{t.inputs}</td><td>{n(t.expected)}</td><td>{n(t.got)}</td><td>± {n(t.tol)}</td><td><span className={`badge ${t.pass ? 'b-0' : 'b-3'}`}>{t.pass ? 'PASS' : 'FAIL'}</span></td></tr>)}</tbody></table></div></div></>}
    </>
  );
}
