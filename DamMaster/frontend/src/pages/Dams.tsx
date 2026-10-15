import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useApi, useCatalog } from '../api/hooks';
import type { Dam, RefItem } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBox, Field, Modal, num, useToast } from '../components/ui';

const PHASES = ['Construction', 'First filling', 'Initial operation', 'Long-term operation'];
const blank: Dam = { id: '', code: '', name: '', damTypeCode: 'CONC_GRAVITY', hazardCode: 'HIGH', isSpecified: true, lifePhase: 'Long-term operation' };

export function DamForm({ initial, onClose, onSaved }: { initial: Dam; onClose: () => void; onSaved: (d: Dam) => void }) {
  const [d, setD] = useState<Dam>(initial); const [err, setErr] = useState<string | null>(null);
  const types = useCatalog<RefItem>('dam-types'); const funcs = useCatalog<RefItem>('dam-functions'); const hz = useCatalog<RefItem>('hazard-classes');
  const set = (k: keyof Dam, v: unknown) => setD({ ...d, [k]: v });
  const n = (k: keyof Dam) => <input className="input" type="number" step="any" value={(d[k] as number | null | undefined) ?? ''} onChange={(e) => set(k, num(e.target.value))} />;
  const save = async () => {
    try { const saved = d.id ? await api.put<Dam>('/dams/' + d.id, d) : await api.post<Dam>('/dams', d); onSaved(saved); }
    catch (e) { setErr((e as Error).message); }
  };
  return (
    <Modal title={d.id ? `Edit ${d.name}` : 'Register dam'} onClose={onClose} wide footer={<><button className="btn" onClick={onClose}>Cancel</button><button className="btn primary" onClick={save}>Save</button></>}>
      <ErrorBox error={err} />
      <div className="form">
        <Field label="Dam code"><input className="input" value={d.code} disabled={!!d.id} onChange={(e) => set('code', e.target.value)} placeholder="DM-001" /></Field>
        <Field label="Name"><input className="input" value={d.name} onChange={(e) => set('name', e.target.value)} /></Field>
        <Field label="Owner"><input className="input" value={d.ownerName ?? ''} onChange={(e) => set('ownerName', e.target.value)} /></Field>
        <Field label="State"><input className="input" value={d.state ?? ''} onChange={(e) => set('state', e.target.value)} /></Field>
        <Field label="District"><input className="input" value={d.district ?? ''} onChange={(e) => set('district', e.target.value)} /></Field>
        <Field label="River"><input className="input" value={d.river ?? ''} onChange={(e) => set('river', e.target.value)} /></Field>
        <Field label="Dam type"><select className="input" value={d.damTypeCode} onChange={(e) => set('damTypeCode', e.target.value)}>{types.map((t) => <option key={t.code} value={t.code}>{t.name}</option>)}</select></Field>
        <Field label="Function"><select className="input" value={d.functionCode ?? ''} onChange={(e) => set('functionCode', e.target.value || null)}><option value="">—</option>{funcs.map((t) => <option key={t.code} value={t.code}>{t.name}</option>)}</select></Field>
        <Field label="Hazard potential"><select className="input" value={d.hazardCode} onChange={(e) => set('hazardCode', e.target.value)}>{hz.map((t) => <option key={t.code} value={t.code}>{t.name}</option>)}</select></Field>
        <Field label="Height (m)">{n('heightM')}</Field><Field label="Crest length (m)">{n('crestLengthM')}</Field><Field label="Gross storage (MCM)">{n('grossStorageMcm')}</Field>
        <Field label="FRL (m)">{n('frlM')}</Field><Field label="MWL (m)">{n('mwlM')}</Field><Field label="MDDL (m)">{n('mddlM')}</Field>
        <Field label="Top of dam (m)">{n('crestLevelM')}</Field>
        <Field label="Seismic zone"><select className="input" value={d.seismicZone ?? ''} onChange={(e) => set('seismicZone', e.target.value || null)}><option value="">—</option>{['II', 'III', 'IV', 'V'].map((z) => <option key={z}>{z}</option>)}</select></Field>
        <Field label="Life phase (sets reading frequency)"><select className="input" value={d.lifePhase} onChange={(e) => set('lifePhase', e.target.value)}>{PHASES.map((p) => <option key={p}>{p}</option>)}</select></Field>
        <Field label="Specified dam (Dam Safety Act)"><select className="input" value={d.isSpecified ? 'Yes' : 'No'} onChange={(e) => set('isSpecified', e.target.value === 'Yes')}><option>Yes</option><option>No</option></select></Field>
        <Field label="EAP status"><select className="input" value={d.eapStatus ?? ''} onChange={(e) => set('eapStatus', e.target.value || null)}><option value="">—</option>{['Approved', 'Revision due', 'Draft', 'Not on file'].map((p) => <option key={p}>{p}</option>)}</select></Field>
        <Field label="Latitude"><input className="input" type="number" step="any" value={d.latitude ?? ''} onChange={(e) => set('latitude', num(e.target.value))} /></Field>
        <Field label="Longitude"><input className="input" type="number" step="any" value={d.longitude ?? ''} onChange={(e) => set('longitude', num(e.target.value))} /></Field>
      </div>
    </Modal>
  );
}

export default function Dams() {
  const { data, error, reload } = useApi<Dam[]>('/dams');
  const { can } = useAuth(); const nav = useNavigate(); const toast = useToast();
  const [edit, setEdit] = useState<Dam | null>(null);
  return (
    <>
      <div className="hero"><div><h1>Dams</h1><p>Master record of every dam: type, hazard, levels and life phase.</p></div>
        {can('manageDams') && <button className="btn primary" onClick={() => setEdit(blank)}>+ Register dam</button>}</div>
      <ErrorBox error={error} />
      <div className="card"><div className="tablewrap"><table>
        <thead><tr><th>Code</th><th>Name</th><th>State</th><th>Type</th><th>Hazard</th><th>Height</th><th>FRL / MWL</th><th>Life phase</th><th>EAP</th></tr></thead>
        <tbody>{data?.map((d) => (
          <tr key={d.id} className="click" onClick={() => nav('/dams/' + d.id)}>
            <td className="mono">{d.code}</td><td><b>{d.name}</b></td><td>{d.state}</td><td>{d.damTypeCode}</td><td>{d.hazardCode}</td><td>{d.heightM ?? '—'} m</td>
            <td>{d.frlM ?? '—'} / {d.mwlM ?? '—'}</td><td>{d.lifePhase}</td><td>{d.eapStatus ?? '—'}</td></tr>))}
          {data?.length === 0 && <tr><td colSpan={9} className="empty">No dams registered.</td></tr>}</tbody>
      </table></div></div>
      {edit && <DamForm initial={edit} onClose={() => setEdit(null)} onSaved={(d) => { setEdit(null); toast(`Saved ${d.name}.`); void reload(); }} />}
    </>
  );
}
