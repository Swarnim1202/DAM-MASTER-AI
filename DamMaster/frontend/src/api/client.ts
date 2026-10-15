// Thin fetch wrapper: session cookie (credentials), CSRF header, JSON in/out, readable errors.
export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message); }
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const res = await fetch('/api' + url, {
    method,
    credentials: 'include',
    headers: { 'X-Requested-With': 'DamMaster', ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}) },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  const data = text ? JSON.parse(text) : undefined;
  if (!res.ok) {
    const fieldErrors = data && data.errors ? Object.entries(data.errors as Record<string, string[]>).map(([k, v]) => `${k}: ${v.join(' ')}`).join(' · ') : '';
    const msg = (data && (data.error || (fieldErrors ? `Please check: ${fieldErrors}` : data.title))) || (res.status === 403 ? 'Your role is not allowed to do this.' : res.status === 401 ? 'Please sign in again.' : `Request failed (${res.status})`);
    throw new ApiError(res.status, msg);
  }
  return data as T;
}

export const api = {
  get: <T,>(url: string) => request<T>('GET', url),
  post: <T,>(url: string, body?: unknown) => request<T>('POST', url, body ?? {}),
  put: <T,>(url: string, body: unknown) => request<T>('PUT', url, body),
  del: <T,>(url: string) => request<T>('DELETE', url),
};

// Times are stored in UTC and shown in IST (Asia/Kolkata).
const istFmt = new Intl.DateTimeFormat('en-IN', { timeZone: 'Asia/Kolkata', day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false });
export const fmtTime = (iso?: string | null) => (iso ? istFmt.format(new Date(iso)) : '—');
export const fmtNum = (v?: number | null, d = 2) => (v === null || v === undefined || Number.isNaN(v) ? '—' : Number(v).toLocaleString('en-IN', { maximumFractionDigits: d }));
