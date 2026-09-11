import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { ThemeContext, type Theme } from '@/lib/theme';

const STORAGE_KEY = 'ucp-theme';

function readStored(): Theme {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return value === 'dark' || value === 'light' ? value : 'system';
  } catch {
    return 'system';
  }
}

function systemTheme(): 'dark' | 'light' {
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

/**
 * Applies the `dark` class on <html>. The inline script in index.html applies the same
 * decision before first paint so there is no flash; this provider keeps it in sync afterwards.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(readStored);
  const [system, setSystem] = useState<'dark' | 'light'>(systemTheme);

  useEffect(() => {
    const query = window.matchMedia('(prefers-color-scheme: dark)');
    const onChange = () => setSystem(systemTheme());
    query.addEventListener('change', onChange);
    return () => query.removeEventListener('change', onChange);
  }, []);

  const resolved = theme === 'system' ? system : theme;

  useEffect(() => {
    const root = document.documentElement;
    root.classList.toggle('dark', resolved === 'dark');
    root.style.colorScheme = resolved;
  }, [resolved]);

  const setTheme = useCallback((next: Theme) => {
    setThemeState(next);
    try {
      if (next === 'system') localStorage.removeItem(STORAGE_KEY);
      else localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // Storage may be unavailable; the in-memory state still applies.
    }
  }, []);

  const value = useMemo(() => ({ theme, resolved, setTheme }), [theme, resolved, setTheme]);
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}
