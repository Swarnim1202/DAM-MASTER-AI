import { useCallback, useEffect, useState } from 'react';
import { api } from './client';

/** Loads data from an API path and exposes reload(); error holds a readable message. */
export function useApi<T>(path: string | null) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const reload = useCallback(async () => {
    if (!path) return;
    setLoading(true);
    try { setData(await api.get<T>(path)); setError(null); } catch (e) { setError((e as Error).message); } finally { setLoading(false); }
  }, [path]);
  useEffect(() => { void reload(); }, [reload]);
  return { data, error, loading, reload, setData };
}

const catalogCache = new Map<string, Promise<unknown>>();
/** Reference catalogues rarely change — cached for the session. */
export function useCatalog<T>(name: string) {
  const [data, setData] = useState<T[]>([]);
  useEffect(() => {
    if (!catalogCache.has(name)) catalogCache.set(name, api.get<T[]>('/reference/' + name));
    (catalogCache.get(name) as Promise<T[]>).then(setData).catch(() => catalogCache.delete(name));
  }, [name]);
  return data;
}
export const clearCatalog = (name: string) => catalogCache.delete(name);
