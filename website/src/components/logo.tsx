import { cn } from '@/lib/utils';

/** The UCP mark: a bracketed prompt chevron, reading as both "terminal" and "bridge". */
export function LogoMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" fill="none" aria-hidden className={cn('size-6', className)}>
      <rect x="1.5" y="1.5" width="29" height="29" rx="7" className="fill-primary" />
      <path
        d="M11 10.5 16.5 16 11 21.5M17.5 21.5h5"
        className="stroke-primary-foreground"
        strokeWidth="2.6"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

export function Wordmark({ className }: { className?: string }) {
  return (
    <span className={cn('inline-flex items-center gap-2.5 font-semibold tracking-tight', className)}>
      <LogoMark />
      <span>
        Unity Control Protocol
      </span>
    </span>
  );
}
