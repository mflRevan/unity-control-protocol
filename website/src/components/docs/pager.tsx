import { ArrowLeft, ArrowRight } from 'lucide-react';
import { Link } from 'react-router-dom';
import type { DocPage } from '@/lib/content';

export function Pager({ prev, next }: { prev?: DocPage; next?: DocPage }) {
  return (
    <nav aria-label="Pagination" className="mt-14 grid gap-3 border-t border-border pt-6 sm:grid-cols-2">
      {prev ? (
        <Link
          to={prev.path}
          className="group flex flex-col gap-1 rounded-lg border border-border p-4 transition-colors hover:border-primary/40 hover:bg-muted/50"
        >
          <span className="inline-flex items-center gap-1 text-xs text-muted-foreground">
            <ArrowLeft className="size-3.5 transition-transform group-hover:-translate-x-0.5" aria-hidden /> Previous
          </span>
          <span className="text-sm font-medium">{prev.title}</span>
        </Link>
      ) : (
        <span />
      )}
      {next && (
        <Link
          to={next.path}
          className="group flex flex-col items-end gap-1 rounded-lg border border-border p-4 text-right transition-colors hover:border-primary/40 hover:bg-muted/50"
        >
          <span className="inline-flex items-center gap-1 text-xs text-muted-foreground">
            Next <ArrowRight className="size-3.5 transition-transform group-hover:translate-x-0.5" aria-hidden />
          </span>
          <span className="text-sm font-medium">{next.title}</span>
        </Link>
      )}
    </nav>
  );
}
