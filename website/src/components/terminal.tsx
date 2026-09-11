import { useReducedMotion } from 'framer-motion';
import { useEffect, useMemo, useRef, useState } from 'react';
import { cn } from '@/lib/utils';

export type Tone = 'ok' | 'warn' | 'error' | 'muted' | 'editor' | 'plain' | 'info';

export interface TerminalLine {
  kind: 'cmd' | 'out';
  text: string;
  tone?: Tone;
  /** Extra pause after this line, in ms. */
  pause?: number;
}

interface TerminalProps {
  script: TerminalLine[];
  title?: string;
  className?: string;
  /** Characters per second while typing commands. */
  speed?: number;
  loop?: boolean;
}

const toneClass: Record<Tone, string> = {
  ok: 'text-ok',
  warn: 'text-warn',
  error: 'text-destructive',
  muted: 'text-terminal-foreground/50',
  editor: 'text-terminal-foreground/55',
  info: 'text-info',
  plain: 'text-terminal-foreground',
};

interface Frame {
  index: number;
  typed: number;
}

/**
 * Plays a recorded-looking terminal session: commands type in, output lines appear with the
 * cadence of a real CLI, then the whole thing loops. Users who prefer reduced motion get the
 * completed transcript.
 */
export function Terminal({ script, title = 'ucp', className, speed = 42, loop = true }: TerminalProps) {
  const reduce = useReducedMotion();
  const [frame, setFrame] = useState<Frame>(() => (reduce ? { index: script.length, typed: 0 } : { index: 0, typed: 0 }));
  const bodyRef = useRef<HTMLDivElement>(null);
  const timer = useRef<number | undefined>(undefined);

  const commandCount = useMemo(() => script.filter((line) => line.kind === 'cmd').length, [script]);

  useEffect(() => {
    if (reduce) return;
    const line = script[frame.index];
    const schedule = (delay: number, next: Frame) => {
      timer.current = window.setTimeout(() => setFrame(next), delay);
    };
    if (!line) {
      if (loop) schedule(3800, { index: 0, typed: 0 });
      return () => window.clearTimeout(timer.current);
    }
    if (line.kind === 'cmd') {
      if (frame.typed < line.text.length) {
        const jitter = 0.6 + Math.random() * 0.9;
        schedule((1000 / speed) * jitter, { index: frame.index, typed: frame.typed + 1 });
      } else {
        schedule(380 + (line.pause ?? 0), { index: frame.index + 1, typed: 0 });
      }
    } else {
      schedule(70 + Math.random() * 90 + (line.pause ?? 0), { index: frame.index + 1, typed: 0 });
    }
    return () => window.clearTimeout(timer.current);
  }, [frame, script, speed, loop, reduce]);

  useEffect(() => {
    const body = bodyRef.current;
    if (body) body.scrollTop = body.scrollHeight;
  }, [frame]);

  const visible = script.slice(0, Math.min(frame.index + 1, script.length));

  return (
    <div
      className={cn(
        'overflow-hidden rounded-xl border border-white/10 bg-terminal text-terminal-foreground shadow-[0_30px_80px_-30px_rgba(0,0,0,0.6)]',
        className,
      )}
      role="img"
      aria-label={`Terminal session showing ${commandCount} ucp commands`}
    >
      <div className="flex h-9 items-center gap-2 border-b border-white/8 px-3.5">
        <span className="size-2.5 rounded-full bg-white/15" />
        <span className="size-2.5 rounded-full bg-white/15" />
        <span className="size-2.5 rounded-full bg-white/15" />
        <span className="ml-2 font-mono text-[11px] text-terminal-foreground/50">{title}</span>
      </div>
      <div ref={bodyRef} className="hide-scrollbar h-[19.5rem] overflow-y-auto px-4 py-3.5 font-mono text-[12.5px] leading-6 sm:text-[13px]">
        {visible.map((line, i) => {
          const isCurrent = i === frame.index;
          if (line.kind === 'cmd') {
            const text = isCurrent && !reduce ? line.text.slice(0, frame.typed) : line.text;
            return (
              <div key={i} className={cn('whitespace-pre-wrap', i > 0 && script[i - 1].kind === 'out' && 'mt-3')}>
                <span className="select-none text-primary">$ </span>
                <span className="text-terminal-foreground">{text}</span>
                {isCurrent && !reduce && <span className="ml-px inline-block h-[1.05em] w-[0.55em] translate-y-[3px] animate-blink bg-terminal-foreground/80" />}
              </div>
            );
          }
          if (isCurrent && !reduce) return null;
          return (
            <div key={i} className={cn('whitespace-pre-wrap', toneClass[line.tone ?? 'plain'])}>
              {line.text}
            </div>
          );
        })}
        {frame.index >= script.length && !reduce && (
          <div className="mt-3">
            <span className="select-none text-primary">$ </span>
            <span className="ml-px inline-block h-[1.05em] w-[0.55em] translate-y-[3px] animate-blink bg-terminal-foreground/80" />
          </div>
        )}
      </div>
    </div>
  );
}
