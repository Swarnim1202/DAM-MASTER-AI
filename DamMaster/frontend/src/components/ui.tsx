import { createContext, useCallback, useContext, useState, type ReactNode } from 'react';

export const LEVEL_NAME = ['Normal', 'Level 1 · Alert', 'Level 2 · Alarm', 'Level 3 · Emergency'];
export const LevelBadge = ({ level }: { level: number }) => <span className={`badge b-${Math.min(Math.max(level, 0), 3)}`}>{LEVEL_NAME[level] ?? level}</span>;
export const QualityBadge = ({ q }: { q?: string | null }) => q ? <span className={`badge ${q === 'G' ? 'b-0' : q === 'S' ? 'b-1' : 'b-3'}`}>{q}</span> : <span className="muted">—</span>;

export function Kpi({ label, value, sub, colour }: { label: string; value: ReactNode; sub?: ReactNode; colour?: string }) {
  return <div className="card kpi" style={colour ? { borderTopColor: colour } : undefined}><small>{label}</small><strong>{value}</strong>{sub && <span>{sub}</span>}</div>;
}

export function Modal({ title, onClose, children, footer, wide }: { title: string; onClose: () => void; children: ReactNode; footer?: ReactNode; wide?: boolean }) {
  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label={title} onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal" style={wide ? { width: 'min(1100px,100%)' } : undefined}>
        <div className="mh"><h3>{title}</h3><button className="x" onClick={onClose} aria-label="Close">×</button></div>
        <div className="mb">{children}</div>
        {footer && <div className="mf">{footer}</div>}
      </div>
    </div>
  );
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return <div className="field"><label>{label}</label>{children}</div>;
}

export const ErrorBox = ({ error }: { error?: string | null }) => (error ? <div className="error" role="alert">{error}</div> : null);

// Simple toast notifications.
const ToastCtx = createContext<(m: string) => void>(() => undefined);
export function ToastProvider({ children }: { children: ReactNode }) {
  const [msg, setMsg] = useState<string | null>(null);
  const show = useCallback((m: string) => { setMsg(m); window.setTimeout(() => setMsg((cur) => (cur === m ? null : cur)), 3500); }, []);
  return <ToastCtx.Provider value={show}>{children}{msg && <div className="toast" role="status">{msg}</div>}</ToastCtx.Provider>;
}
export const useToast = () => useContext(ToastCtx);

/** Converts '' to null and numeric strings to numbers for form submission. */
export const num = (v: string): number | null => (v.trim() === '' || Number.isNaN(Number(v)) ? null : Number(v));
