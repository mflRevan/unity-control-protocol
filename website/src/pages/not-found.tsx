import { Link } from 'react-router-dom';
import { useMeta } from '@/lib/use-meta';

export function NotFound() {
  useMeta({ title: 'Not found' });
  return (
    <div className="container-x flex flex-1 flex-col items-center justify-center py-32 text-center">
      <p className="font-mono text-sm text-muted-foreground">404</p>
      <h1 className="mt-3 text-2xl font-semibold tracking-tight">There is nothing at this address.</h1>
      <p className="mt-2 max-w-md text-sm text-muted-foreground">
        The page may have moved when the docs were restructured. The index lists every page, and agents can start from llms.txt.
      </p>
      <div className="mt-6 flex gap-2 text-sm">
        <Link to="/docs" className="rounded-md bg-primary px-3 py-1.5 font-medium text-primary-foreground hover:bg-primary/90">
          Documentation
        </Link>
        <a href="/llms.txt" className="rounded-md border border-border px-3 py-1.5 font-medium hover:bg-muted">
          llms.txt
        </a>
      </div>
    </div>
  );
}
