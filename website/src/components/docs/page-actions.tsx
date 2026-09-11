import { ExternalLink, FileText } from 'lucide-react';
import { CopyButton } from '@/components/copy-button';
import { loadMarkdown } from '@/lib/markdown';

interface PageActionsProps {
  /** Site-relative raw Markdown path, e.g. /docs/quickstart.md */
  md: string;
  github: string;
}

/** Copy-for-LLM, raw Markdown, and edit links shown above every doc and skill. */
export function PageActions({ md, github }: PageActionsProps) {
  return (
    <div className="flex flex-wrap items-center gap-1.5 text-xs">
      <CopyButton text={() => loadMarkdown(md)} label="Copy as Markdown" showLabel />
      <a
        href={md}
        className="inline-flex h-7 items-center gap-1.5 rounded-md border border-border/80 px-2 font-medium text-muted-foreground transition-colors hover:border-border hover:text-foreground"
      >
        <FileText className="size-3.5" aria-hidden /> View raw
      </a>
      <a
        href={github}
        target="_blank"
        rel="noreferrer"
        className="inline-flex h-7 items-center gap-1.5 rounded-md border border-border/80 px-2 font-medium text-muted-foreground transition-colors hover:border-border hover:text-foreground"
      >
        <ExternalLink className="size-3.5" aria-hidden /> Edit on GitHub
      </a>
    </div>
  );
}
