import { useEffect, useState } from 'react';

const cache = new Map<string, Promise<string>>();

/** Fetches a raw Markdown file served from /public (the same files agents read). */
export function loadMarkdown(url: string): Promise<string> {
  let pending = cache.get(url);
  if (!pending) {
    pending = fetch(url, { headers: { Accept: 'text/markdown, text/plain' } }).then((response) => {
      if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
      return response.text();
    });
    pending.catch(() => cache.delete(url));
    cache.set(url, pending);
  }
  return pending;
}

export interface MarkdownState {
  markdown: string | null;
  error: string | null;
  loading: boolean;
}

interface Loaded {
  url: string;
  markdown: string | null;
  error: string | null;
}

export function useMarkdown(url: string | null): MarkdownState {
  const [loaded, setLoaded] = useState<Loaded | null>(null);

  useEffect(() => {
    if (!url) return;
    let cancelled = false;
    loadMarkdown(url).then(
      (markdown) => {
        if (!cancelled) setLoaded({ url, markdown, error: null });
      },
      (error: Error) => {
        if (!cancelled) setLoaded({ url, markdown: null, error: error.message });
      },
    );
    return () => {
      cancelled = true;
    };
  }, [url]);

  if (!url) return { markdown: null, error: null, loading: false };
  if (loaded && loaded.url === url) return { markdown: loaded.markdown, error: loaded.error, loading: false };
  return { markdown: null, error: null, loading: true };
}

export interface Frontmatter {
  data: Record<string, string>;
  body: string;
}

/** Splits an Agent Skills SKILL.md into its frontmatter map and body. Nested keys are flattened as `metadata.version`. */
export function splitFrontmatter(markdown: string): Frontmatter {
  const match = /^---\r?\n([\s\S]*?)\r?\n---\r?\n?/.exec(markdown);
  if (!match) return { data: {}, body: markdown };
  const data: Record<string, string> = {};
  let parent: string | null = null;
  let folded: string | null = null;
  for (const raw of match[1].split(/\r?\n/)) {
    if (folded && /^ {2}\S/.test(raw)) {
      data[folded] = (data[folded] ? `${data[folded]} ` : '') + raw.trim();
      continue;
    }
    folded = null;
    const nested = /^ {2}([\w-]+): (.*)$/.exec(raw);
    if (nested && parent) {
      data[`${parent}.${nested[1]}`] = unquote(nested[2]);
      continue;
    }
    const top = /^([\w-]+):(?: (.*))?$/.exec(raw);
    if (!top) continue;
    const [, key, value] = top;
    if (value === undefined || value === '') {
      parent = key;
    } else if (value === '>-' || value === '>' || value === '|') {
      folded = key;
      parent = null;
      data[key] = '';
    } else {
      data[key] = unquote(value);
      parent = null;
    }
  }
  return { data, body: markdown.slice(match[0].length) };
}

function unquote(value: string): string {
  const trimmed = value.trim();
  return /^(['"]).*\1$/.test(trimmed) ? trimmed.slice(1, -1) : trimmed;
}
