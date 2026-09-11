import type { HighlighterCore } from 'shiki/core';

const LANGS: Record<string, () => Promise<unknown>> = {
  bash: () => import('@shikijs/langs/bash'),
  json: () => import('@shikijs/langs/json'),
  csharp: () => import('@shikijs/langs/csharp'),
  yaml: () => import('@shikijs/langs/yaml'),
  xml: () => import('@shikijs/langs/xml'),
  powershell: () => import('@shikijs/langs/powershell'),
  markdown: () => import('@shikijs/langs/markdown'),
  diff: () => import('@shikijs/langs/diff'),
  toml: () => import('@shikijs/langs/toml'),
};

const ALIASES: Record<string, string> = {
  sh: 'bash',
  shell: 'bash',
  zsh: 'bash',
  console: 'bash',
  text: 'text',
  txt: 'text',
  cs: 'csharp',
  yml: 'yaml',
  uxml: 'xml',
  uss: 'text',
  md: 'markdown',
  ps1: 'powershell',
  pwsh: 'powershell',
  jsonc: 'json',
};

export function normalizeLang(lang: string | undefined): string {
  if (!lang) return 'text';
  const lower = lang.toLowerCase();
  const alias = ALIASES[lower] ?? lower;
  return alias in LANGS ? alias : 'text';
}

let highlighter: Promise<HighlighterCore> | null = null;
const loaded = new Set<string>();

async function getHighlighter(): Promise<HighlighterCore> {
  if (!highlighter) {
    highlighter = (async () => {
      const [{ createHighlighterCore }, { createJavaScriptRegexEngine }, light, dark] = await Promise.all([
        import('shiki/core'),
        import('shiki/engine/javascript'),
        import('@shikijs/themes/vitesse-light'),
        import('@shikijs/themes/vitesse-dark'),
      ]);
      return createHighlighterCore({
        themes: [light.default, dark.default],
        langs: [],
        engine: createJavaScriptRegexEngine({ forgiving: true }),
      });
    })();
  }
  return highlighter;
}

/** Returns highlighted HTML for a fenced block, or null for plain text (render as-is). */
export async function highlight(code: string, lang: string): Promise<string | null> {
  const normalized = normalizeLang(lang);
  if (normalized === 'text') return null;
  const shiki = await getHighlighter();
  if (!loaded.has(normalized)) {
    const module = (await LANGS[normalized]()) as { default: Parameters<HighlighterCore['loadLanguage']>[0] };
    await shiki.loadLanguage(module.default);
    loaded.add(normalized);
  }
  return shiki.codeToHtml(code, {
    lang: normalized,
    themes: { light: 'vitesse-light', dark: 'vitesse-dark' },
    defaultColor: false,
  });
}
