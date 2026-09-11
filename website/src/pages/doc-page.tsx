import { useLocation } from 'react-router-dom';
import { Markdown } from '@/components/markdown';
import { PageActions } from '@/components/docs/page-actions';
import { Pager } from '@/components/docs/pager';
import { TableOfContents } from '@/components/docs/toc';
import { PageTransition } from '@/components/motion';
import { docNeighbours, findDoc, groupOf } from '@/lib/content';
import { useMarkdown } from '@/lib/markdown';
import { useMeta } from '@/lib/use-meta';
import { NotFound } from '@/pages/not-found';

export function DocPage() {
  const { pathname } = useLocation();
  const page = findDoc(pathname);
  const { markdown, error, loading } = useMarkdown(page ? page.md : null);
  useMeta({
    title: page ? page.title : 'Not found',
    description: page?.description,
    path: page?.path,
    markdown: page?.md,
  });

  if (!page) return <NotFound />;
  const { prev, next } = docNeighbours(page);
  const group = groupOf(page);

  return (
    <div className="flex min-w-0 flex-1 gap-10">
      <article className="min-w-0 flex-1 py-8 md:py-10">
        <PageTransition key={page.path}>
          <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
            <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">{group?.title}</p>
            <PageActions md={page.md} github={page.github} />
          </div>
          {loading && !markdown && <DocSkeleton />}
          {error && (
            <div className="rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm">
              <p className="font-medium">Could not load this page.</p>
              <p className="mt-1 text-muted-foreground">
                {error}. The raw file is at{' '}
                <a href={page.md} className="text-primary underline">
                  {page.md}
                </a>
                .
              </p>
            </div>
          )}
          {markdown && <Markdown markdown={markdown} />}
          <Pager prev={prev} next={next} />
        </PageTransition>
      </article>
      <aside className="sticky top-14 hidden h-[calc(100svh-3.5rem)] w-52 shrink-0 self-start overflow-y-auto py-10 xl:block">
        <TableOfContents headings={page.headings} />
      </aside>
    </div>
  );
}

function DocSkeleton() {
  return (
    <div className="animate-pulse space-y-4" aria-hidden>
      <div className="h-8 w-2/3 rounded bg-muted" />
      <div className="h-4 w-full rounded bg-muted" />
      <div className="h-4 w-11/12 rounded bg-muted" />
      <div className="h-4 w-4/5 rounded bg-muted" />
      <div className="mt-8 h-32 w-full rounded-lg bg-muted" />
      <div className="h-4 w-full rounded bg-muted" />
      <div className="h-4 w-3/4 rounded bg-muted" />
    </div>
  );
}
