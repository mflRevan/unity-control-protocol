import { ArrowRight, Boxes, Layers } from 'lucide-react';
import { Link } from 'react-router-dom';
import { InstallTabs } from '@/components/install-tabs';
import { PageTransition, Reveal, Stagger, StaggerItem } from '@/components/motion';
import { install, omniSkill, surfaceSkills, version } from '@/lib/content';
import { useMeta } from '@/lib/use-meta';
import type { Skill } from '@/lib/content';

const installTabs = [
  {
    id: 'claude',
    label: 'Claude Code',
    lang: 'text',
    code: [install.claudeCode.marketplace, `${install.claudeCode.omni}            # omni skill`, `${install.claudeCode.surfaces}   # eight surface skills`].join('\n'),
    note: 'Skills surface as /ucp:unity-control-protocol and /ucp-surfaces:ucp-<surface>. Run /plugin update to pick up new releases.',
  },
  {
    id: 'skills',
    label: 'skills.sh',
    code: [`${install.skillsSh}                  # pick interactively`, `${install.skillsSh} --skill unity-control-protocol -y`, 'npx skills update'].join('\n'),
    note: 'Installs into the right directory for Codex, Cursor, Copilot, Gemini CLI, opencode, Amp, and others, and records it in skills-lock.json.',
  },
  {
    id: 'gh',
    label: 'GitHub CLI',
    code: ['gh skill install mflRevan/unity-control-protocol unity-control-protocol --agent codex --scope user', 'gh skill update'].join('\n'),
    note: 'GitHub CLI 2.90 or newer.',
  },
  {
    id: 'manual',
    label: 'Manual',
    code: ['mkdir -p .agents/skills/unity-control-protocol', 'curl -fsSL https://unityctl.dev/skills/unity-control-protocol.md \\', '  -o .agents/skills/unity-control-protocol/SKILL.md'].join('\n'),
    note: 'Every skill is served raw with its frontmatter, so a plain download is a valid install.',
  },
];

export function SkillsIndex() {
  useMeta({
    title: 'Agent skills',
    description: 'Agent Skills for the Unity Control Protocol: one omni skill and eight surface-scoped skills, installable in Claude Code, Codex, Cursor, Copilot, Gemini CLI, opencode, and Amp.',
    path: '/skills',
    markdown: '/skills/index.md',
  });

  return (
    <PageTransition>
      <div className="container-x py-12 md:py-16">
        <Reveal mode="mount" className="max-w-2xl">
          <p className="text-xs font-medium uppercase tracking-wider text-primary">Agent skills · v{version}</p>
          <h1 className="mt-2 text-3xl font-semibold tracking-tight md:text-4xl">The manual an agent loads when a task touches Unity.</h1>
          <p className="mt-4 text-base leading-7 text-muted-foreground">
            Each skill follows the{' '}
            <a href="https://agentskills.io/specification" target="_blank" rel="noreferrer" className="text-primary underline decoration-primary/30 underline-offset-[3px]">
              Agent Skills specification
            </a>
            , so any harness that implements it can use them unchanged. The files under <code className="font-mono text-[0.9em]">skills/</code> in the
            repository are the ground truth; these pages, the raw endpoints, and the Claude Code plugins are generated from them.
          </p>
        </Reveal>

        <Reveal delay={0.1} className="mt-10">
          <InstallTabs tabs={installTabs} />
        </Reveal>

        {omniSkill && (
          <Reveal className="mt-14">
            <SectionTitle icon={Layers} title="The omni skill" hint="Recommended default. One activation carries every cross-surface workflow." />
            <SkillCard skill={omniSkill} featured />
          </Reveal>
        )}

        <div className="mt-14">
          <Reveal>
            <SectionTitle
              icon={Boxes}
              title="Surface skills"
              hint="Narrow, predictable activations. Each names its commands so the agent loads only what the task needs, and each defers to the omni skill for anything broader."
            />
          </Reveal>
          <Stagger className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {surfaceSkills.map((skill) => (
              <StaggerItem key={skill.name}>
                <SkillCard skill={skill} />
              </StaggerItem>
            ))}
          </Stagger>
        </div>

        <Reveal className="mt-16 rounded-xl border border-border bg-muted/40 p-6">
          <h2 className="text-sm font-semibold">For agents reading this site directly</h2>
          <ul className="mt-3 grid gap-2 text-sm text-muted-foreground sm:grid-cols-2">
            <li>
              <a href="/skills/index.json" className="font-mono text-primary">
                /skills/index.json
              </a>{' '}
              is the machine-readable catalog.
            </li>
            <li>
              <a href="/skills/index.md" className="font-mono text-primary">
                /skills/index.md
              </a>{' '}
              is the same catalog as Markdown.
            </li>
            <li>
              <span className="font-mono text-foreground">/skills/&lt;name&gt;.md</span> is any skill, frontmatter included.
            </li>
            <li>
              <a href="/llms.txt" className="font-mono text-primary">
                /llms.txt
              </a>{' '}
              and{' '}
              <a href="/llms-full.txt" className="font-mono text-primary">
                /llms-full.txt
              </a>{' '}
              index and bundle the documentation.
            </li>
          </ul>
        </Reveal>
      </div>
    </PageTransition>
  );
}

function SectionTitle({ icon: Icon, title, hint }: { icon: typeof Layers; title: string; hint: string }) {
  return (
    <div className="mb-5">
      <h2 className="inline-flex items-center gap-2 text-lg font-semibold tracking-tight">
        <Icon className="size-4 text-primary" aria-hidden />
        {title}
      </h2>
      <p className="mt-1 max-w-2xl text-sm text-muted-foreground">{hint}</p>
    </div>
  );
}

export function SkillCard({ skill, featured = false }: { skill: Skill; featured?: boolean }) {
  return (
    <Link
      to={skill.path}
      className="group flex h-full flex-col rounded-xl border border-border bg-card p-5 transition-all hover:-translate-y-0.5 hover:border-primary/40 hover:shadow-[0_12px_40px_-20px_color-mix(in_oklch,var(--color-primary)_50%,transparent)]"
    >
      <div className="flex items-start justify-between gap-3">
        <h3 className="font-mono text-sm font-semibold">{skill.name}</h3>
        <ArrowRight className="size-4 shrink-0 text-muted-foreground transition-transform group-hover:translate-x-0.5 group-hover:text-primary" aria-hidden />
      </div>
      <p className={featured ? 'mt-2 text-sm leading-6 text-muted-foreground' : 'mt-2 line-clamp-4 text-sm leading-6 text-muted-foreground'}>
        {skill.description}
      </p>
      <div className="mt-auto flex flex-wrap gap-1.5 pt-4">
        {skill.commands.slice(0, featured ? 40 : 8).map((command) => (
          <span key={command} className="rounded-md border border-border bg-muted/60 px-1.5 py-0.5 font-mono text-[11px] text-muted-foreground">
            {command}
          </span>
        ))}
        {!featured && skill.commands.length > 8 && (
          <span className="px-1 py-0.5 font-mono text-[11px] text-muted-foreground">+{skill.commands.length - 8}</span>
        )}
      </div>
    </Link>
  );
}
