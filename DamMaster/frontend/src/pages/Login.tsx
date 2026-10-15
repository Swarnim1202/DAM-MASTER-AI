import { useEffect, useState, type FormEvent } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ErrorBox, Field } from '../components/ui';

export default function Login() {
  const { login } = useAuth();
  const nav = useNavigate();
  const [u, setU] = useState(''); const [p, setP] = useState(''); const [busy, setBusy] = useState(false);
  const [qs] = useSearchParams(); const [err, setErr] = useState<string | null>(qs.get('sso_error'));
  const [sso, setSso] = useState<{ enabled: boolean; label: string; localLogin: boolean } | null>(null);
  useEffect(() => { fetch('/api/auth/sso/config').then((r) => r.json()).then(setSso).catch(() => setSso(null)); }, []);
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setBusy(true); setErr(null);
    try { await login(u, p); nav('/'); } catch (ex) { setErr((ex as Error).message); } finally { setBusy(false); }
  };
  return (
    <div className="login">
      <div className="brandside">
        <div><h1>DAM MASTER AI</h1><p>Dam safety monitoring: instrument data, validation, four-level alerts, failure-mode analysis and a reference library — in one system.</p>
          <ul><li>Excel and IoT gateway data with quality checks</li><li>Threshold, statistical, rate and corroboration alerts</li><li>PFMA register linked to instruments</li><li>Role-based access and full audit trail</li></ul></div>
        <small>Rodic Consultants Pvt. Ltd. · Digital vertical</small>
      </div>
      <div className="formside">
        <form onSubmit={submit} aria-label="Sign in">
          <h2>Sign in</h2>
          <ErrorBox error={err} />
          <Field label="Username"><input className="input" autoComplete="username" value={u} onChange={(e) => setU(e.target.value)} required /></Field>
          <Field label="Password"><input className="input" type="password" autoComplete="current-password" value={p} onChange={(e) => setP(e.target.value)} required /></Field>
          <button className="btn primary" style={{ width: '100%', padding: 10 }} disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button>
          {sso?.enabled && <><div className="muted small" style={{ textAlign: 'center', margin: '10px 0' }}>or</div>
            <a className="btn" style={{ width: '100%', padding: 10, display: 'block', textAlign: 'center', boxSizing: 'border-box' }} href="/api/auth/sso/login?returnUrl=/">{sso.label}</a>
            {!sso.localLogin && <p className="muted small">Password sign-in is reserved for administrators (emergency access).</p>}</>}
          <p className="muted small">Accounts are created by the administrator. Five wrong attempts lock the account for 15 minutes.</p>
        </form>
      </div>
    </div>
  );
}
