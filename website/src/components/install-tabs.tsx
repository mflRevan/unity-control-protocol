import { motion, useReducedMotion } from 'framer-motion';
import { useId, useState } from 'react';
import { CodeBlock } from '@/components/code-block';
import { cn } from '@/lib/utils';

export interface InstallTab {
  id: string;
  label: string;
  code: string;
  lang?: string;
  note?: string;
}

export function InstallTabs({ tabs, className }: { tabs: InstallTab[]; className?: string }) {
  const [active, setActive] = useState(tabs[0]?.id);
  const reduce = useReducedMotion();
  const baseId = useId();
  const current = tabs.find((tab) => tab.id === active) ?? tabs[0];

  return (
    <div className={cn('overflow-hidden rounded-xl border border-border bg-card', className)}>
      <div role="tablist" aria-label="Install method" className="thin-scroll flex overflow-x-auto border-b border-border">
        {tabs.map((tab) => {
          const selected = tab.id === current.id;
          return (
            <button
              key={tab.id}
              role="tab"
              id={`${baseId}-${tab.id}`}
              aria-selected={selected}
              aria-controls={`${baseId}-${tab.id}-panel`}
              onClick={() => setActive(tab.id)}
              className={cn(
                'relative shrink-0 px-4 py-2.5 text-sm text-muted-foreground transition-colors hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring',
                selected && 'text-foreground',
              )}
            >
              {tab.label}
              {selected && (
                <motion.span
                  layoutId={reduce ? undefined : `${baseId}-underline`}
                  className="absolute inset-x-3 -bottom-px h-0.5 rounded-full bg-primary"
                  transition={{ type: 'spring', stiffness: 500, damping: 40 }}
                />
              )}
            </button>
          );
        })}
      </div>
      <div role="tabpanel" id={`${baseId}-${current.id}-panel`} aria-labelledby={`${baseId}-${current.id}`}>
        <CodeBlock code={current.code} lang={current.lang ?? 'bash'} className="rounded-none border-0" />
        {current.note && <p className="border-t border-border px-4 py-2.5 text-xs text-muted-foreground">{current.note}</p>}
      </div>
    </div>
  );
}
