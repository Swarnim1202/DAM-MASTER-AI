import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, fmtTime } from '../api/client';
import type { NotificationRow } from '../api/types';
import { LevelBadge } from './ui';

/** In-app notification bell: polls the unread count every 60 s. */
export default function Bell() {
  const [count, setCount] = useState(0); const [open, setOpen] = useState(false); const [items, setItems] = useState<NotificationRow[]>([]);
  const poll = useCallback(async () => { try { setCount((await api.get<{ unread: number }>('/notifications/mine/count')).unread); } catch { /* signed out */ } }, []);
  useEffect(() => { void poll(); const t = window.setInterval(poll, 60000); return () => window.clearInterval(t); }, [poll]);
  const toggle = async () => { if (!open) setItems(await api.get<NotificationRow[]>('/notifications/mine')); setOpen(!open); };
  const readAll = async () => { await api.post('/notifications/mine/read', {}); setCount(0); setItems(items.map((i) => ({ ...i, readAt: i.readAt ?? new Date().toISOString() }))); };
  return (
    <div style={{ position: 'relative' }}>
      <button className="btn sm" onClick={toggle} aria-label={`Notifications, ${count} unread`}>🔔{count > 0 && <span className="badge b-3" style={{ marginLeft: 4 }}>{count}</span>}</button>
      {open && <div className="card" style={{ position: 'absolute', right: 0, top: 38, width: 380, maxHeight: 440, overflow: 'auto', zIndex: 40, boxShadow: '0 16px 40px rgba(10,30,50,.2)' }}>
        <div className="row" style={{ justifyContent: 'space-between', marginBottom: 6 }}><b>Notifications</b>{count > 0 && <button className="btn sm" onClick={readAll}>Mark all read</button>}</div>
        {items.length === 0 && <p className="muted small">Nothing yet.</p>}
        {items.map((n) => <Link key={n.id} to="/alerts" onClick={() => setOpen(false)} style={{ display: 'block', padding: '8px 4px', borderTop: '1px solid var(--line)', textDecoration: 'none', color: 'inherit', opacity: n.readAt ? 0.65 : 1 }}>
          <LevelBadge level={n.level} /> <span className="small"><b>{n.subject.replace('[DAM MASTER] ', '')}</b></span><div className="muted small">{fmtTime(n.createdAt)}{n.escalationStep ? ` · escalation step ${n.escalationStep}` : ''}</div></Link>)}
      </div>}
    </div>
  );
}
