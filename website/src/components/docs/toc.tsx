import { useEffect, useState } from 'react';
import type { Heading } from '@/lib/content';
import { cn } from '@/lib/utils';

/** In-page table of contents that tracks the heading currently in view. */
export function TableOfContents({ headings, className }: { headings: Heading[]; className?: string }) {
  const items = headings.filter((heading) => heading.depth === 2 || heading.depth === 3);
  const [active, setActive] = useState<string | null>(null);

  useEffect(() => {
    if (items.length === 0) return;
    const elements = items.map((item) => document.getElementById(item.id)).filter((el): el is HTMLElement => Boolean(el));
    if (elements.length === 0) return;
    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries.filter((entry) => entry.isIntersecting).sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top);
        if (visible[0]) setActive(visible[0].target.id);
      },
      { rootMargin: '-80px 0px -70% 0px', threshold: [0, 1] },
    );
    for (const element of elements) observer.observe(element);
    return () => observer.disconnect();
  }, [items]);

  if (items.length < 2) return null;

  return (
    <nav aria-label="On this page" className={cn('text-[13px]', className)}>
      <h2 className="mb-2 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">On this page</h2>
      <ul className="space-y-1 border-l border-border">
        {items.map((item) => (
          <li key={item.id}>
            <a
              href={`#${item.id}`}
              className={cn(
                '-ml-px block border-l border-transparent py-0.5 pl-3 leading-5 text-muted-foreground transition-colors hover:text-foreground',
                item.depth === 3 && 'pl-6',
                active === item.id && 'border-primary text-foreground',
              )}
            >
              {item.text}
            </a>
          </li>
        ))}
      </ul>
    </nav>
  );
}
