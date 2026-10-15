import { useState } from 'react';
import { api, fmtTime } from '../api/client';
import { clearCatalog, useApi, useCatalog } from '../api/hooks';
import type { Interpretation, RefItem, Standard } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBox, Field, Modal, useToast } from '../components/ui';

type Tab = 'dam-types' | 'failure-modes' | 'parameters' | 'instrument-types' | 'interpretations' | 'alert-levels' | 'dhi-components' | 'standards';
const TABS: [Tab, string][] = [['dam-types', 'Dam types'], ['failure-modes', 'Failure modes'], ['parameters', 'Parameters'], ['instrument-types', 'Instruments & sensors'], ['interpretations', 'What changes mean'], ['alert-levels', 'Alert levels'], ['dhi-components', 'DHI method'], ['standards', 'Codes & standards']];
const COLS: Record<Exclude<Tab, 'standards' | 'interpretations'>, [string, string][]> = {
  'dam-types': [['name', 'Type'], ['materialGroup', 'Group'], ['description', 'How it holds water'], ['weaknesses', 'Weaknesses to watch'], ['monitoringPriority', 'Monitoring priority']],
  'failure-modes': [['name', 'Failure mode'], ['description', 'What happens'], ['appliesTo', 'Dam types'], ['earlySigns', 'Early warning signs']],
  parameters: [['code', 'Code'], ['name', 'Parameter'], ['groupCode', 'Group'], ['unit', 'Unit'], ['purpose', 'Why it is measured']],
  'instrument-types': [['name', 'Instrument'], ['measures', 'Measures'], ['principle', 'Principle'], ['rawOutput', 'Raw output'], ['engUnit', 'Unit'], ['typicalRange', 'Typical range'], ['usedIn', 'Used in']],
  'dhi-components': [['name', 'Component'], ['weight', 'Weight'], ['description', 'How it is scored']],
  'alert-levels': [['level', 'Level'], ['name', 'Name'], ['meaning', 'Meaning'], ['notifyRoles', 'Notify'], ['channels', 'Channels'], ['ackMinutes', 'Acknowledge within (min)']],
};

function Catalog({ name }: { name: Exclude<Tab, 'standards' | 'interpretations'> }) {
  const rows = useCatalog<RefItem>(name);
  return <div className="tablewrap"><table><thead><tr>{COLS[name].map(([, h]) => <th key={h}>{h}</th>)}</tr></thead>
    <tbody>{rows.map((r, i) => <tr key={i}>{COLS[name].map(([k]) => <td key={k} className={k === 'name' || k === 'code' ? '' : 'small'}>{k === 'name' ? <b>{String(r[k] ?? '')}</b> : String(r[k] ?? '')}</td>)}</tr>)}</tbody></table></div>;
}

function Standards() {
  const { data, reload, error } = useApi<Standard[]>('/reference/standards'); const { can } = useAuth(); const toast = useToast();
  const [v, setV] = useState<Standard | null>(null); const [ed, setEd] = useState(''); const [cl, setCl] = useState(''); const [err, setErr] = useState<string | null>(null);
  const verify = async () => { try { await api.post(`/reference/standards/${v!.code}/verify`, { edition: ed, clauses: cl }); setV(null); clearCatalog('standards'); toast('Standard verified.'); void reload(); } catch (e) { setErr((e as Error).message); } };
  const revoke = async (s: Standard) => { await api.del(`/reference/standards/${s.code}/verify`); toast('Verification revoked.'); void reload(); };
  return <>
    <ErrorBox error={error} />
    <p className="note">Titles are as commonly cited. The software never invents an edition or clause: a Dam Safety Engineer or Approver records the edition in force and the clauses relied on, and the verification is audit-logged.</p>
    <div className="tablewrap mt"><table><thead><tr><th>Category</th><th>Reference</th><th>Subject</th><th>Edition</th><th>Clauses relied on</th><th>Status</th><th></th></tr></thead>
      <tbody>{data?.map((s) => <tr key={s.code}><td className="small">{s.category}</td><td><b>{s.title}</b></td><td className="small">{s.subject}</td><td>{s.edition ?? '—'}</td><td className="small">{s.clauses ?? '—'}</td>
        <td>{s.verifiedBy ? <span className="badge b-0" title={fmtTime(s.verifiedAt)}>Verified · {s.verifiedBy}</span> : <span className="badge b-3">Verification required</span>}</td>
        <td>{can('verifyStandards') && (s.verifiedBy ? <button className="btn sm" onClick={() => revoke(s)}>Revoke</button> : <button className="btn sm" onClick={() => { setV(s); setEd(s.edition ?? ''); setCl(s.clauses ?? ''); setErr(null); }}>Verify</button>)}</td></tr>)}</tbody></table></div>
    {v && <Modal title={`Verify ${v.title}`} onClose={() => setV(null)} footer={<><button className="btn" onClick={() => setV(null)}>Cancel</button><button className="btn primary" onClick={verify}>Verify</button></>}>
      <ErrorBox error={err} /><Field label="Edition / year in force (from the official publication)"><input className="input" value={ed} onChange={(e) => setEd(e.target.value)} /></Field>
      <Field label="Clauses / tables relied on by the system"><input className="input" value={cl} onChange={(e) => setCl(e.target.value)} /></Field></Modal>}
  </>;
}

function Interpretations() {
  const rows = useCatalog<Interpretation>('interpretations'); const groups = useCatalog<RefItem>('parameter-groups');
  return <div className="tablewrap"><table><thead><tr><th>Group</th><th>Observation</th><th>Normal pattern</th><th>Warning sign</th><th>Critical sign</th><th>Possible cause</th><th>Action</th></tr></thead>
    <tbody>{rows.map((r) => <tr key={r.id}><td className="small">{groups.find((g) => g.code === r.groupCode)?.name ?? r.groupCode}</td><td><b>{r.observation}</b></td><td className="small">{r.normalPattern}</td><td className="small">{r.warningSign}</td><td className="small">{r.criticalSign}</td><td className="small">{r.possibleCause}</td><td className="small">{r.actionText}</td></tr>)}</tbody></table></div>;
}

export default function Library() {
  const [tab, setTab] = useState<Tab>('dam-types');
  return (
    <>
      <div className="hero"><div><h1>Reference library</h1><p>The dam-safety knowledge the system runs on (document RCPL-DMAI-REF-001): dam types, failure modes, parameters, instruments, interpretation of changes, alert levels and code references.</p></div></div>
      <div className="card"><div className="tabs" role="tablist">{TABS.map(([k, l]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setTab(k)}>{l}</button>)}</div>
        {tab === 'standards' ? <Standards /> : tab === 'interpretations' ? <Interpretations /> : <Catalog name={tab} />}</div>
    </>
  );
}
