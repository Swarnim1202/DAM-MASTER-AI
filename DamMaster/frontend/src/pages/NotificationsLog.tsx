import { useState } from 'react';
import { api, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { NotificationRow } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBox, LevelBadge, useToast } from '../components/ui';

const STATUS: Record<string, string> = { Delivered: 'b-0', Sent: 'b-0', Simulated: 'b-info', Queued: 'b-1', Failed: 'b-3' };

export default function NotificationsLog() {
  const { can } = useAuth(); const toast = useToast();
  const [channel, setChannel] = useState(''); const [status, setStatus] = useState('');
  const { data, error, reload } = useApi<NotificationRow[]>(`/notifications/log?channel=${channel}&status=${status}`);
  const runNow = async () => { try { const r = await api.post<{ escalated: number; dispatched: number }>('/notifications/run'); toast(`${r.escalated} escalated, ${r.dispatched} dispatched.`); void reload(); } catch (e) { toast((e as Error).message); } };
  return (
    <>
      <div className="hero"><div><h1>Notifications</h1><p>Every in-app, e-mail and SMS message sent for alerts, including escalations. Without an SMTP server or SMS gateway configured, messages are recorded as <b>Simulated</b>.</p></div>
        <div className="row"><select className="input" style={{ width: 'auto' }} value={channel} onChange={(e) => setChannel(e.target.value)} aria-label="Channel"><option value="">All channels</option><option>InApp</option><option>Email</option><option>SMS</option></select>
          <select className="input" style={{ width: 'auto' }} value={status} onChange={(e) => setStatus(e.target.value)} aria-label="Status"><option value="">All statuses</option>{Object.keys(STATUS).map((s) => <option key={s}>{s}</option>)}</select>
          {can('runNotifications') && <button className="btn" onClick={runNow}>Run escalation &amp; dispatch now</button>}</div></div>
      <ErrorBox error={error} />
      <div className="card"><div className="tablewrap" style={{ maxHeight: '70vh' }}><table><thead><tr><th>Created (IST)</th><th>Level</th><th>Channel</th><th>Recipient</th><th>Subject</th><th>Escalation</th><th>Status</th></tr></thead>
        <tbody>{data?.map((n) => <tr key={n.id}><td>{fmtTime(n.createdAt)}</td><td><LevelBadge level={n.level} /></td><td>{n.channel}</td><td>{n.recipientName}<div className="muted small mono">{n.channel === 'InApp' ? 'in-app' : n.recipient}</div></td>
          <td className="small">{n.subject}</td><td>{n.escalationStep ? `step ${n.escalationStep}` : 'initial'}</td><td><span className={`badge ${STATUS[n.status] ?? 'b-mute'}`}>{n.status}</span>{n.error && <div className="muted small">{n.error}</div>}</td></tr>)}
          {data?.length === 0 && <tr><td colSpan={7} className="empty">No notifications yet.</td></tr>}</tbody></table></div></div>
    </>
  );
}
