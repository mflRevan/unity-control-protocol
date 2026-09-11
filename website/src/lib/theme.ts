import { createContext, useContext } from 'react';

export type Theme = 'dark' | 'light' | 'system';

export interface ThemeContextValue {
  theme: Theme;
  resolved: 'dark' | 'light';
  setTheme: (theme: Theme) => void;
}

export const ThemeContext = createContext<ThemeContextValue>({ theme: 'system', resolved: 'dark', setTheme: () => {} });

export const useTheme = () => useContext(ThemeContext);
