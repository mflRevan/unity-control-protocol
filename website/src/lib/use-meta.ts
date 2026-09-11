import { useEffect } from 'react';
import { site } from '@/lib/content';

interface Meta {
  title: string;
  description?: string;
  /** Path of the canonical human page, e.g. /docs/quickstart. */
  path?: string;
  /** Raw Markdown twin of this page, advertised to agents via <link rel="alternate">. */
  markdown?: string;
}

const SUFFIX = 'Unity Control Protocol';

function setMeta(selector: string, attribute: string, value: string) {
  let element = document.head.querySelector<HTMLMetaElement | HTMLLinkElement>(selector);
  if (!element) {
    const [tag] = selector.split('[');
    element = document.createElement(tag) as HTMLMetaElement | HTMLLinkElement;
    const attrs = [...selector.matchAll(/\[([\w:-]+)="([^"]*)"\]/g)];
    for (const [, name, val] of attrs) element.setAttribute(name, val);
    document.head.appendChild(element);
  }
  element.setAttribute(attribute, value);
}

export function useMeta({ title, description, path, markdown }: Meta) {
  useEffect(() => {
    const full = title === SUFFIX ? title : `${title} · ${SUFFIX}`;
    document.title = full;
    setMeta('meta[property="og:title"]', 'content', full);
    if (description) {
      setMeta('meta[name="description"]', 'content', description);
      setMeta('meta[property="og:description"]', 'content', description);
    }
    if (path) {
      setMeta('link[rel="canonical"]', 'href', `${site}${path}`);
      setMeta('meta[property="og:url"]', 'content', `${site}${path}`);
    }
    const alternate = document.head.querySelector('link[rel="alternate"][type="text/markdown"]');
    if (markdown) {
      setMeta('link[rel="alternate"][type="text/markdown"]', 'href', `${site}${markdown}`);
    } else if (alternate) {
      alternate.remove();
    }
  }, [title, description, path, markdown]);
}
