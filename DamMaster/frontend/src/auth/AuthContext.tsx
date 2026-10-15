import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import { api } from '../api/client';
import type { User } from '../api/types';
import { allowed, type Policy } from './roles';

interface AuthState { user: User | null; loading: boolean; login: (u: string, p: string) => Promise<void>; logout: () => Promise<void>; refresh: () => Promise<void>; can: (p: Policy) => boolean }
const Ctx = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const refresh = useCallback(async () => {
    try { const u = await api.get<User>('/auth/me'); setUser(u); localStorage.setItem('dm.lastUser', JSON.stringify(u)); }
    catch (e) {
      // offline (network failure, not a 401): keep the last user so field inspections work without signal; the server still checks on sync
      const cached = localStorage.getItem('dm.lastUser');
      if (e instanceof TypeError && cached) setUser({ ...JSON.parse(cached), offline: true }); else { setUser(null); localStorage.removeItem('dm.lastUser'); }
    } finally { setLoading(false); }
  }, []);
  useEffect(() => { void refresh(); }, [refresh]);
  const login = async (username: string, password: string) => { const u = await api.post<User>('/auth/login', { username, password }); setUser(u); localStorage.setItem('dm.lastUser', JSON.stringify(u)); };
  const logout = async () => { try { await api.post('/auth/logout'); } finally { setUser(null); localStorage.removeItem('dm.lastUser'); } };
  return <Ctx.Provider value={{ user, loading, login, logout, refresh, can: (p) => allowed(user?.role, p) }}>{children}</Ctx.Provider>;
}

export function useAuth() {
  const c = useContext(Ctx);
  if (!c) throw new Error('useAuth outside AuthProvider');
  return c;
}
