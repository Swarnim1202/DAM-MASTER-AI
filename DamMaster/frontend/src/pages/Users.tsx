import { useState } from 'react';
import { api, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { Role, User } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { POLICY, ROLE_LABEL } from '../auth/roles';
import { ErrorBox, Field, Modal, useToast } from '../components/ui';

const ROLES = Object.keys(ROLE_LABEL) as Role[];

export default function Users() {
  const { data, error, reload } = useApi<User[]>('/users'); const { user: me } = useAuth(); const toast = useToast();
  const [add, setAdd] = useState(false); const [f, setF] = useState({ username: '', fullName: '', role: 'DamSafetyEngineer' as Role, password: '', email: '', phone: '' });
  const [contact, setContact] = useState<User | null>(null); const [err, setErr] = useState<string | null>(null);
  const [temp, setTemp] = useState<string | null>(null);
  const update = async (u: User, patch: Partial<User>) => { try { await api.put('/users/' + u.id, { fullName: patch.fullName ?? u.fullName, role: patch.role ?? u.role, isActive: patch.isActive ?? u.isActive, email: patch.email !== undefined ? patch.email : u.email, phone: patch.phone !== undefined ? patch.phone : u.phone }); toast('User updated.'); void reload(); } catch (e) { toast((e as Error).message); void reload(); } };
  const create = async () => { try { await api.post('/users', f); setAdd(false); toast('User created — they must change the password at first sign-in.'); void reload(); } catch (e) { setErr((e as Error).message); } };
  const reset = async (u: User) => { try { const r = await api.post<{ temporaryPassword: string }>(`/users/${u.id}/reset-password`); setTemp(`${u.username}: ${r.temporaryPassword}`); } catch (e) { toast((e as Error).message); } };
  return (
    <>
      <div className="hero"><div><h1>Users &amp; roles</h1><p>Engineering authority (approve thresholds, close alerts, verify codes) is separate from data entry and administration.</p></div><button className="btn primary" onClick={() => { setAdd(true); setErr(null); }}>+ Add user</button></div>
      <ErrorBox error={error} />
      {temp && <div className="note" style={{ marginBottom: 12 }}>Temporary password — give it to the user securely, it is shown only once: <b className="mono">{temp}</b> <button className="btn sm" onClick={() => setTemp(null)}>Hide</button></div>}
      <div className="card"><div className="tablewrap"><table><thead><tr><th>Username</th><th>Name</th><th>Role</th><th>Contact (alerts)</th><th>Status</th><th>Last sign-in (IST)</th><th></th></tr></thead>
        <tbody>{data?.map((u) => <tr key={u.id}><td className="mono">{u.username}</td><td>{u.fullName}</td>
          <td><select className="input" style={{ width: 'auto' }} value={u.role} disabled={u.id === me?.id} onChange={(e) => update(u, { role: e.target.value as Role })} aria-label={'Role of ' + u.username}>{ROLES.map((r) => <option key={r} value={r}>{ROLE_LABEL[r]}</option>)}</select></td>
          <td className="small">{u.email ?? '—'}<div className="mono">{u.phone ?? ''}</div><button className="btn sm" onClick={() => setContact({ ...u })}>Edit</button></td>
          <td><span className={`badge ${u.isActive ? 'b-0' : 'b-mute'}`}>{u.isActive ? 'Active' : 'Disabled'}</span>{u.mustChangePassword && <div className="muted small">must change password</div>}</td><td>{fmtTime(u.lastLoginAt)}</td>
          <td className="row"><button className="btn sm" disabled={u.id === me?.id} onClick={() => update(u, { isActive: !u.isActive })}>{u.isActive ? 'Disable' : 'Enable'}</button><button className="btn sm" onClick={() => reset(u)}>Reset password</button></td></tr>)}</tbody></table></div></div>
      <div className="card mt"><h2>Permission matrix</h2><div className="tablewrap"><table><thead><tr><th>Permission</th>{ROLES.map((r) => <th key={r}>{ROLE_LABEL[r].replace(' (read-only)', '')}</th>)}</tr></thead>
        <tbody>{Object.entries(POLICY).map(([p, rs]) => <tr key={p}><td>{p.replace(/([A-Z])/g, ' $1').toLowerCase()}</td>{ROLES.map((r) => <td key={r}>{(rs as readonly string[]).includes(r) ? <span className="badge b-0">✓</span> : ''}</td>)}</tr>)}</tbody></table></div>
        <p className="muted small">Every signed-in role can view dashboards, dams, sensors, alerts and the reference library.</p></div>
      {add && <Modal title="Add user" onClose={() => setAdd(false)} footer={<><button className="btn" onClick={() => setAdd(false)}>Cancel</button><button className="btn primary" onClick={create}>Create</button></>}>
        <ErrorBox error={err} /><div className="form two">
          <Field label="Username"><input className="input" value={f.username} onChange={(e) => setF({ ...f, username: e.target.value })} /></Field>
          <Field label="Full name"><input className="input" value={f.fullName} onChange={(e) => setF({ ...f, fullName: e.target.value })} /></Field>
          <Field label="Role"><select className="input" value={f.role} onChange={(e) => setF({ ...f, role: e.target.value as Role })}>{ROLES.map((r) => <option key={r} value={r}>{ROLE_LABEL[r]}</option>)}</select></Field>
          <Field label="E-mail (alert e-mails)"><input className="input" type="email" value={f.email} onChange={(e) => setF({ ...f, email: e.target.value })} /></Field>
          <Field label="Mobile (Level 2+ SMS)"><input className="input" value={f.phone} onChange={(e) => setF({ ...f, phone: e.target.value })} /></Field>
          <Field label="Temporary password (8+, letters and digits)"><input className="input" type="password" autoComplete="new-password" value={f.password} onChange={(e) => setF({ ...f, password: e.target.value })} /></Field></div></Modal>}
      {contact && <Modal title={`Contact details — ${contact.username}`} onClose={() => setContact(null)} footer={<><button className="btn" onClick={() => setContact(null)}>Cancel</button><button className="btn primary" onClick={async () => { const c = contact; setContact(null); await update(c, { fullName: c.fullName, email: c.email || null, phone: c.phone || null }); }}>Save</button></>}>
        <div className="form two"><Field label="Full name"><input className="input" value={contact.fullName} onChange={(e) => setContact({ ...contact, fullName: e.target.value })} /></Field>
          <Field label="E-mail"><input className="input" type="email" value={contact.email ?? ''} onChange={(e) => setContact({ ...contact, email: e.target.value })} /></Field>
          <Field label="Mobile"><input className="input" value={contact.phone ?? ''} onChange={(e) => setContact({ ...contact, phone: e.target.value })} /></Field></div></Modal>}
    </>
  );
}
