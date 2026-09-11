import { Check, Copy } from 'lucide-react';
import { useCallback, useEffect, useRef, useState } from 'react';
import { cn } from '@/lib/utils';

interface CopyButtonProps {
  /** Text to copy, or a function that resolves it lazily (e.g. fetching a raw file). */
  text: string | (() => Promise<string>);
  label?: string;
  className?: string;
  /** Show the label text next to the icon. */
  showLabel?: boolean;
}

export function CopyButton({ text, label = 'Copy', className, showLabel = false }: CopyButtonProps) {
  const [copied, setCopied] = useState(false);
  const timer = useRef<number | undefined>(undefined);

  useEffect(() => () => window.clearTimeout(timer.current), []);

  const copy = useCallback(async () => {
    try {
      const value = typeof text === 'function' ? await text() : text;
      await navigator.clipboard.writeText(value);
      setCopied(true);
      window.clearTimeout(timer.current);
      timer.current = window.setTimeout(() => setCopied(false), 1600);
    } catch {
      setCopied(false);
    }
  }, [text]);

  return (
    <button
      type="button"
      onClick={copy}
      aria-label={copied ? 'Copied' : label}
      title={copied ? 'Copied' : label}
      className={cn(
        'inline-flex h-7 items-center gap-1.5 rounded-md border border-border/80 bg-background/70 px-2 text-xs font-medium text-muted-foreground backdrop-blur transition-colors hover:border-border hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring',
        copied && 'text-ok hover:text-ok',
        className,
      )}
    >
      {copied ? <Check className="size-3.5" aria-hidden /> : <Copy className="size-3.5" aria-hidden />}
      {showLabel && <span>{copied ? 'Copied' : label}</span>}
    </button>
  );
}
