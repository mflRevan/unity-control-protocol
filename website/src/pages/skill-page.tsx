import { ArrowLeft, Download } from 'lucide-react';
import { useMemo } from 'react';
import { Link, useParams } from 'react-router-dom';
import { CodeBlock } from '@/components/code-block';
import { CopyButton } from '@/components/copy-button';
import { PageActions } from '@/components/docs/page-actions';
import { TableOfContents } from '@/components/docs/toc';
import { Markdown } from '@/components/markdown';
import { PageTransition } from '@/components/motion';
import { findSkill, site } from '@/lib/content';
import { loadMarkdown, splitFrontmatter, useMarkdown } from '@/lib/markdown';
import { useMeta } from '@/lib/use-meta';
import { NotFound } from '@/pages/not-found';

export function SkillPage() {
  const { name = '' } = useParams();
  const skill = findSkill(name);
  const { markdown, error, loading } = useMarkdown(skill ? skill.md : null);
  const parsed = useMemo(() => (markdown ? splitFrontmatter(markdown) : null), [markdown]);

  useMeta({
    title: skill ? skill.name : 'Skill not found',
    description: skill?.description,
    path: skill?.path,
    markdown: skill?.md,
  });

  if (!skill) return <NotFound />;

  const manual = [
    `mkdir -p .agents/skills/${skill.name}`,
    `curl -fsSL ${site}${skill.md} -o .agents/skills/${skill.name}/SKILL.md`,
  ].join('\n');

  return (
    <PageTransition>
      <div className="container-x flex gap-12 py-10">
        <article className="min-w-0 flex-1">
          <Link to="/skills" className="inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground">
            <ArrowLeft className="size-3.5" aria-hidden /> All skills
          </Link>
          <div className="mt-4 flex flex-wrap items-start justify-between gap-4">
            <div>
              <p className="text-xs font-medium uppercase tracking-wider text-primary">{skill.kind === 'omni' ? 'Omni skill' : 'Surface skill'} · v{skill.version}</p>
              <h1 className="mt-1 font-mono text-2xl font-semibold tracking-tight md:text-3xl">{skill.name}</h1>
            </div>
            <PageActions md={skill.md} github={skill.github} />
          </div>

          <div className="mt-6 grid gap-4 rounded-xl border border-border bg-card p-5 sm:grid-cols-[1fr_auto]">
            <dl className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-[max-content_1fr]">
              <dt className="text-muted-foreground">Description</dt>
              <dd className="leading-6">{skill.description}</dd>
              {skill.compatibility && (
                <>
                  <dt className="text-muted-foreground">Compatibility</dt>
                  <dd className="leading-6">{skill.compatibility}</dd>
                </>
              )}
              <dt className="text-muted-foreground">Commands</dt>
              <dd className="flex flex-wrap gap-1.5">
                {skill.commands.map((command) => (
                  <span key={command} className="rounded-md border border-border bg-muted/60 px-1.5 py-0.5 font-mono text-[11px]">
                    ucp {command}
                  </span>
                ))}
              </dd>
              {parsed?.data['metadata.author'] && (
                <>
                  <dt className="text-muted-foreground">Author</dt>
                  <dd>{parsed.data['metadata.author']}</dd>
                </>
              )}
            </dl>
            <div className="flex flex-col gap-2 sm:items-end">
              <a
                href={skill.md}
                download={`${skill.name}.SKILL.md`}
                className="inline-flex h-8 items-center gap-1.5 rounded-md bg-primary px-3 text-sm font-medium text-primary-foreground hover:bg-primary/90"
              >
                <Download className="size-3.5" aria-hidden /> Download SKILL.md
              </a>
              <CopyButton text={() => loadMarkdown(skill.md)} label="Copy SKILL.md" showLabel className="h-8 px-3 text-sm" />
            </div>
          </div>

          <div className="mt-6">
            <CodeBlock code={manual} lang="bash" title="manual install" />
          </div>

          <div className="mt-10">
            {loading && !markdown && <p className="text-sm text-muted-foreground">Loading skill…</p>}
            {error && (
              <p className="text-sm text-destructive">
                Could not load the skill: {error}. Raw file:{' '}
                <a href={skill.md} className="underline">
                  {skill.md}
                </a>
              </p>
            )}
            {parsed && <Markdown markdown={parsed.body} />}
          </div>
        </article>
        <aside className="sticky top-14 hidden h-[calc(100svh-3.5rem)] w-52 shrink-0 self-start overflow-y-auto py-10 xl:block">
          <TableOfContents headings={skill.headings} />
        </aside>
      </div>
    </PageTransition>
  );
}
