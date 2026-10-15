import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { ErrorBox, Field, useToast } from '../components/ui';

export default function ChangePassword() {
  const { user, refresh } = useAuth(); const toast = useToast(); const nav = useNavigate();
  const [c, setC] = useState(''); const [n, setN] = useState(''); const [r, setR] = useState(''); const [err, setErr] = useState<string | null>(null);
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setErr(null);
    if (n !== r) { setErr('The new passwords do not match.'); return; }
    try { await api.post('/auth/change-password', { currentPassword: c, newPassword: n }); await refresh(); toast('Password changed.'); nav('/'); }
    catch (ex) { setErr((ex as Error).message); }
  };
  return (
    <div className="card" style={{ maxWidth: 460 }}>
      <h2>Change password</h2>
      {user?.mustChangePassword && <div className="note" style={{ marginBottom: 10 }}>You must set a new password before continuing.</div>}
      <ErrorBox error={err} />
      <form onSubmit={submit}>
        <Field label="Current password"><input className="input" type="password" autoComplete="current-password" value={c} onChange={(e) => setC(e.target.value)} required /></Field>
        <Field label="New password (8+ characters, letters and digits)"><input className="input" type="password" autoComplete="new-password" value={n} onChange={(e) => setN(e.target.value)} required /></Field>
        <Field label="Repeat new password"><input className="input" type="password" autoComplete="new-password" value={r} onChange={(e) => setR(e.target.value)} required /></Field>
        <button className="btn primary">Change password</button>
      </form>
    </div>
  );
}
