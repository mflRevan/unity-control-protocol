import { motion, useReducedMotion } from 'framer-motion';
import {
  ArrowRight,
  Boxes,
  Camera,
  Check,
  Cpu,
  FlaskConical,
  Gauge,
  GitBranch,
  LayoutTemplate,
  Minus,
  Package,
  Play,
  Terminal as TerminalIcon,
} from 'lucide-react';
import { Link } from 'react-router-dom';
import { CodeBlock } from '@/components/code-block';
import { CopyButton } from '@/components/copy-button';
import { InstallTabs } from '@/components/install-tabs';
import { PageTransition, Reveal, Stagger, StaggerItem } from '@/components/motion';
import { ease } from '@/lib/motion-presets';
import { Terminal, type TerminalLine } from '@/components/terminal';
import { install, version } from '@/lib/content';
import { useMeta } from '@/lib/use-meta';
import { cn } from '@/lib/utils';

// Every line below is real CLI output from the Fantasy Kingdom demo project.
const heroScript: TerminalLine[] = [
  { kind: 'cmd', text: 'ucp connect' },
  { kind: 'out', text: '[OK] Connected to Unity bridge', tone: 'ok' },
  { kind: 'out', text: '  | Unity 6000.4.0f1', tone: 'muted' },
  { kind: 'out', text: '  | Protocol: 0.6.3', tone: 'muted' },
  { kind: 'out', text: '  | Main thread: responsive (last tick 84 ms ago)', tone: 'muted', pause: 500 },
  { kind: 'cmd', text: 'ucp object create Crate --primitive Cube' },
  { kind: 'out', text: "[OK] Created 'Crate' (id: -769436)", tone: 'ok' },
  { kind: 'out', text: '[editor] edit mode · scene Demo (dirty) · console 0 errors, 2 warnings', tone: 'editor', pause: 400 },
  { kind: 'cmd', text: 'ucp view isolate --name Crate --views front,right -o crate.png' },
  { kind: 'out', text: '[OK] Saved crate-front.png', tone: 'ok' },
  { kind: 'out', text: '[OK] Saved crate-right.png', tone: 'ok' },
  { kind: 'out', text: '[editor] edit mode · scene Demo (dirty) · console 0 errors, 2 warnings', tone: 'editor', pause: 500 },
  { kind: 'cmd', text: 'ucp scene save && ucp play' },
  { kind: 'out', text: '[OK] Saved active scene: Assets/Scenes/Demo.unity', tone: 'ok' },
  { kind: 'out', text: '[OK] Entered play mode', tone: 'ok' },
  { kind: 'out', text: '[editor] play mode · scene Demo · console clean', tone: 'editor', pause: 400 },
  { kind: 'cmd', text: 'ucp record capture --duration 5 --slowdown 4 -o run.mp4' },
  { kind: 'out', text: '[OK] Recording finalized: run.mp4', tone: 'ok', pause: 200 },
  { kind: 'out', text: '[editor] play mode · scene Demo · console clean', tone: 'editor', pause: 1400 },
];

const surfaces = [
  { icon: Boxes, title: 'Scenes and objects', blurb: 'Hierarchy, primitives, serialized properties, transforms, spatial queries, prefabs. All with Undo.', command: 'ucp object set --name Crate --prop mass=4', to: '/docs/authoring/objects', skill: 'ucp-scene-authoring' },
  { icon: Package, title: 'Assets and materials', blurb: 'Search, inspect, and edit assets, importers, materials, and references without breaking GUIDs.', command: 'ucp material set Crate --color _BaseColor=#c0392b', to: '/docs/authoring/assets', skill: 'ucp-assets' },
  { icon: LayoutTemplate, title: 'UI Toolkit', blurb: 'Lint UXML and USS with Unity’s importers, populate scenarios, inspect resolved layout, capture panels.', command: 'ucp ui screenshot Assets/UI/Treasury.uxml --state prosperous', to: '/docs/authoring/ui-toolkit', skill: 'ucp-ui-toolkit' },
  { icon: Camera, title: 'Screenshots and video', blurb: 'Composed renders a vision model can read, orbit views, and clips that slow the game down for the camera.', command: 'ucp record capture --duration 5 --slowdown 4', to: '/docs/runtime/logs-and-media', skill: 'ucp-visual-feedback' },
  { icon: Play, title: 'Play mode and tests', blurb: 'Compile, enter play, pause, step, run edit and play mode tests, and read the console back as data.', command: 'ucp run-tests --mode play --filter Inventory', to: '/docs/runtime/play-mode', skill: 'ucp-runtime-debugging' },
  { icon: Gauge, title: 'Profiler', blurb: 'Profiler sessions, frame timings, hierarchy, and callstacks as JSON instead of screenshots of a window.', command: 'ucp profile capture --frames 120 --top 20', to: '/docs/runtime/profiler', skill: 'ucp-runtime-debugging' },
  { icon: Cpu, title: 'Project and builds', blurb: 'Packages, player and quality settings, scripting defines, build targets and builds.', command: 'ucp build run --target StandaloneWindows64', to: '/docs/project/build', skill: 'ucp-project-config' },
  { icon: GitBranch, title: 'Version control', blurb: 'Unity VCS status, checkout, and checkin when the native cm client is not around.', command: 'ucp vcs status', to: '/docs/project/version-control', skill: 'ucp-version-control' },
];

interface ShowcaseItem {
  kind: 'video' | 'image';
  src: string;
  poster?: string;
  width: number;
  height: number;
  span: 'full' | 'half';
  /** Stage aspect when the capture should sit inside a larger frame instead of filling it. */
  stage?: [number, number];
  title: string;
  caption: string;
  command: string;
}

// Each capture is shown at its own aspect ratio, never cropped; the grid pairs the two 16:9 stills.
const showcase: ShowcaseItem[] = [
  {
    kind: 'video',
    src: '/media/kingdom-flythrough.webm',
    poster: '/media/kingdom-flythrough.jpg',
    width: 1280,
    height: 648,
    span: 'full',
    title: 'A camera move, recorded from the CLI',
    caption: 'The demo project is Unity’s Fantasy Kingdom sample moved to HDRP. The camera path is set through ucp exec and captured with ucp record.',
    command: 'ucp play && ucp record capture --duration 11 --fps 30 --width 1920 --height 1080 --bitrate-kbps 24000 -o flythrough.mp4',
  },
  {
    kind: 'image',
    src: '/media/kingdom-overview.webp',
    width: 1600,
    height: 900,
    span: 'half',
    title: 'Game view screenshot',
    caption: 'What the player sees, straight to disk. Screenshots work in edit mode and play mode.',
    command: 'ucp screenshot --width 1920 --height 1080 -o overview.png',
  },
  {
    kind: 'image',
    src: '/media/ui-treasury.webp',
    width: 780,
    height: 460,
    span: 'half',
    stage: [16, 9],
    title: 'UI Toolkit panel, populated',
    caption: 'A UXML document rendered off-screen with scenario data, deterministic to the pixel.',
    command: 'ucp ui screenshot Assets/UI/Kingdom/Treasury.ucp-ui.json --state prosperous -o treasury.png',
  },
  {
    kind: 'image',
    src: '/media/isolate.webp',
    width: 2720,
    height: 900,
    span: 'full',
    title: 'Isolated multi-view render',
    caption: 'One object, three angles, framed from its bounds. Built for a vision model that has to judge geometry.',
    command: 'ucp view isolate --name Preset_House_Windmill_01 --views front,right,back --max-edge 900 -o windmill.png',
  },
];

const compare = [
  { feature: 'Scene hierarchy, objects, components, serialized properties', unity: 'catalog', ucp: 'yes, with Undo' },
  { feature: 'Transforms and spatial queries (raycast, bounds, ground)', unity: false, ucp: true },
  { feature: 'Composed renders a vision model can read', unity: 'screenshot', ucp: true },
  { feature: 'Video capture, event-triggered, model-paced', unity: false, ucp: true },
  { feature: 'UI Toolkit lint, inspect, populate, screenshot', unity: false, ucp: 'Unity 6' },
  { feature: 'Profiler sessions, frames, hierarchy, callstacks', unity: 'planned', ucp: true },
  { feature: 'Editor state on every command (mode, dirty, console)', unity: false, ucp: true },
  { feature: 'Modal dialog detection and answering', unity: false, ucp: true },
  { feature: 'Editor installs, licensing, project scaffolding', unity: true, ucp: false },
];

const agentInstall = [
  { id: 'claude', label: 'Claude Code', lang: 'text', code: [install.claudeCode.marketplace, install.claudeCode.omni].join('\n') },
  { id: 'skills', label: 'Codex, Cursor, Copilot, Gemini, opencode', code: `${install.skillsSh} --skill unity-control-protocol -y` },
  { id: 'gh', label: 'GitHub CLI', code: 'gh skill install mflRevan/unity-control-protocol unity-control-protocol' },
  { id: 'curl', label: 'curl', code: 'curl -fsSL https://unityctl.dev/skills/unity-control-protocol.md \\\n  -o .agents/skills/unity-control-protocol/SKILL.md' },
];

export function Landing() {
  useMeta({
    title: 'Unity Control Protocol',
    description: 'The Unity Editor as a command line. Scenes, objects, assets, play mode, tests, profiler, builds, screenshots and video from a terminal or an AI agent.',
    path: '/',
    markdown: '/index.md',
  });

  return (
    <PageTransition>
      <Hero />
      <Surfaces />
      <Agents />
      <Showcase />
      <Compare />
      <Architecture />
      <QuickStart />
    </PageTransition>
  );
}

function Hero() {
  const reduce = useReducedMotion();
  const installCommand = 'npm install -g @mflrevan/ucp';
  return (
    <section className="relative overflow-hidden">
      <div className="grid-paper pointer-events-none absolute inset-0 -z-10" aria-hidden />
      <div className="container-x grid items-center gap-12 py-16 md:py-24 lg:grid-cols-[1.05fr_1fr] lg:gap-16">
        <div>
          <Reveal mode="mount">
            <Link
              to={`/docs/runtime/editor-state`}
              className="inline-flex items-center gap-2 rounded-full border border-border bg-background/60 py-1 pr-3 pl-1 text-xs text-muted-foreground transition-colors hover:text-foreground"
            >
              <span className="rounded-full bg-primary px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-primary-foreground">v{version}</span>
              Editor state on every command, modal dialogs answered
              <ArrowRight className="size-3" aria-hidden />
            </Link>
          </Reveal>
          <Reveal mode="mount" delay={0.06}>
            <h1 className="mt-6 text-4xl font-semibold tracking-[-0.03em] text-balance sm:text-5xl lg:text-[3.4rem] lg:leading-[1.05]">
              The Unity Editor as a command line.
            </h1>
          </Reveal>
          <Reveal mode="mount" delay={0.12}>
            <p className="mt-5 max-w-xl text-lg leading-8 text-muted-foreground text-pretty">
              Scenes, objects, assets, materials, prefabs, UI Toolkit, play mode, tests, profiler, builds, screenshots and video. From a terminal, a
              script, CI, or an AI agent. Runs on your machine against the editor you already have open.
            </p>
          </Reveal>
          <Reveal mode="mount" delay={0.18}>
            <div className="mt-8 flex flex-wrap items-center gap-3">
              <div className="flex h-10 items-center gap-3 rounded-lg border border-border bg-card pr-1.5 pl-3.5 font-mono text-[13px] shadow-sm">
                <span className="select-none text-muted-foreground">$</span>
                <span>{installCommand}</span>
                <CopyButton text={installCommand} className="h-7" />
              </div>
              <Link
                to="/docs/quickstart"
                className="inline-flex h-10 items-center gap-1.5 rounded-lg bg-primary px-4 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
              >
                Quick start <ArrowRight className="size-4" aria-hidden />
              </Link>
              <Link to="/skills" className="inline-flex h-10 items-center rounded-lg border border-border px-4 text-sm font-medium transition-colors hover:bg-muted">
                Agent skills
              </Link>
            </div>
          </Reveal>
          <Reveal mode="mount" delay={0.24}>
            <ul className="mt-8 flex flex-wrap gap-x-6 gap-y-2 text-xs text-muted-foreground">
              {['Unity 2021.3 to 6.6', 'Windows, macOS, Linux', 'MIT, no cloud, no account', 'Rust CLI, ~15 ms per call'].map((item) => (
                <li key={item} className="inline-flex items-center gap-1.5">
                  <Check className="size-3.5 text-ok" aria-hidden /> {item}
                </li>
              ))}
            </ul>
          </Reveal>
        </div>
        <motion.div
          initial={reduce ? false : { opacity: 0, y: 24, scale: 0.98 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          transition={{ duration: 0.7, ease, delay: 0.15 }}
          className="lg:justify-self-end lg:w-full"
        >
          <Terminal script={heroScript} title="ucp — Fantasy Kingdom (HDRP)" />
        </motion.div>
      </div>
    </section>
  );
}

function SectionHeading({ eyebrow, title, blurb, className }: { eyebrow: string; title: string; blurb?: string; className?: string }) {
  return (
    <Reveal className={cn('max-w-2xl', className)}>
      <p className="text-xs font-semibold uppercase tracking-wider text-primary">{eyebrow}</p>
      <h2 className="mt-2 text-2xl font-semibold tracking-tight text-balance md:text-3xl">{title}</h2>
      {blurb && <p className="mt-3 text-base leading-7 text-muted-foreground text-pretty">{blurb}</p>}
    </Reveal>
  );
}

function Showcase() {
  return (
    <section className="border-t border-border/70 bg-surface">
      <div className="container-x py-16 md:py-24">
        <SectionHeading
          eyebrow="See it work"
          title="Every artifact below was produced by a command."
          blurb="No editor screenshots by hand. Each capture, render, and clip came out of the CLI against the demo project, exactly as an agent would get it."
        />
        <Stagger className="mt-10 grid gap-5 md:grid-cols-2">
          {showcase.map((item) => (
            <StaggerItem key={item.src} className={cn('flex', item.span === 'full' && 'md:col-span-2')}>
              <figure className="flex w-full flex-col overflow-hidden rounded-xl border border-border bg-card">
                <div
                  className={cn('flex w-full items-center justify-center bg-terminal', item.stage && 'p-[6%]')}
                  style={{ aspectRatio: item.stage ? `${item.stage[0]} / ${item.stage[1]}` : `${item.width} / ${item.height}` }}
                >
                  {item.kind === 'video' ? (
                    <video
                      className="block size-full"
                      src={item.src}
                      poster={item.poster}
                      width={item.width}
                      height={item.height}
                      autoPlay
                      muted
                      loop
                      playsInline
                      preload="metadata"
                      aria-label={item.title}
                    />
                  ) : (
                    <img
                      className={cn('block', item.stage ? 'max-h-full max-w-full rounded-md shadow-[0_20px_50px_-20px_rgba(0,0,0,0.8)]' : 'size-full')}
                      src={item.src}
                      alt={item.title}
                      width={item.width}
                      height={item.height}
                      loading="lazy"
                      decoding="async"
                    />
                  )}
                </div>
                <figcaption className="flex flex-1 flex-col border-t border-border p-4">
                  <h3 className="text-sm font-semibold">{item.title}</h3>
                  <p className="mt-1 flex-1 text-sm leading-6 text-muted-foreground">{item.caption}</p>
                  <div className="mt-3 flex items-center gap-2 rounded-md border border-border bg-code px-3 py-1.5 font-mono text-[12px]">
                    <span className="select-none text-primary">$</span>
                    <span className="thin-scroll min-w-0 flex-1 overflow-x-auto whitespace-nowrap py-0.5">{item.command}</span>
                    <CopyButton text={item.command} className="h-6 border-transparent bg-transparent px-1" />
                  </div>
                </figcaption>
              </figure>
            </StaggerItem>
          ))}
        </Stagger>
      </div>
    </section>
  );
}

function Surfaces() {
  return (
    <section className="border-t border-border/70 bg-surface">
      <div className="container-x py-16 md:py-24">
        <SectionHeading
          eyebrow="Command surface"
          title="Everything behind the editor's windows, as commands."
          blurb="Each surface has a documentation section and a matching agent skill. Every command supports --json, exits non-zero on failure, and reports the editor's state."
        />
        <Stagger className="mt-10 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {surfaces.map((surface) => (
            <StaggerItem key={surface.title} className="h-full">
              <div className="group flex h-full flex-col rounded-xl border border-border bg-card p-5 transition-colors hover:border-primary/40">
                <surface.icon className="size-5 text-primary" aria-hidden />
                <h3 className="mt-4 text-sm font-semibold">{surface.title}</h3>
                <p className="mt-1.5 text-sm leading-6 text-muted-foreground">{surface.blurb}</p>
                <code className="mt-4 block truncate rounded-md border border-border bg-code px-2.5 py-1.5 font-mono text-[11.5px] text-foreground/80" title={surface.command}>
                  {surface.command}
                </code>
                <div className="mt-auto flex items-center gap-3 pt-4 text-xs">
                  <Link to={surface.to} className="inline-flex items-center gap-1 font-medium text-primary hover:underline">
                    Docs <ArrowRight className="size-3" aria-hidden />
                  </Link>
                  <Link to={`/skills/${surface.skill}`} className="text-muted-foreground hover:text-foreground">
                    Skill
                  </Link>
                </div>
              </div>
            </StaggerItem>
          ))}
        </Stagger>
      </div>
    </section>
  );
}

function Agents() {
  const stateLine = `$ ucp object set --name Player --prop speed=8
[OK] Set speed on Player
[editor] play mode (paused) · scene Demo (dirty) · console 2 errors, 4 warnings`;
  return (
    <section className="border-t border-border/70">
      <div className="container-x grid gap-12 py-16 md:py-24 lg:grid-cols-2 lg:gap-16">
        <div>
          <SectionHeading
            eyebrow="Built for agents"
            title="An agent should never have to guess what the editor is doing."
            blurb="Every response carries a one-line summary of the editor: play or edit mode, whether the scene is dirty, how many errors and warnings sit in the console. Modal dialogs that would freeze the editor are detected and can be answered from the CLI."
          />
          <Reveal delay={0.1} className="mt-8">
            <CodeBlock code={stateLine} lang="text" title="every bridge command" noCopy />
          </Reveal>
          <Reveal delay={0.15}>
            <ul className="mt-8 space-y-3 text-sm">
              {[
                ['Structured output', '--json on every command, stable exit codes, and errors that say what to do next.'],
                ['Vision-ready captures', 'Isolated renders, orbit views, and slowed-down recordings a model can actually read.'],
                ['Recovery built in', 'Compile-aware waits, lost-response tolerance across domain reloads, dialog answering, force close.'],
                ['Skills as ground truth', 'One omni skill plus eight surface skills, generated into every marketplace from the same files.'],
              ].map(([title, text]) => (
                <li key={title} className="flex gap-3">
                  <Check className="mt-1 size-4 shrink-0 text-ok" aria-hidden />
                  <span>
                    <span className="font-medium">{title}.</span> <span className="text-muted-foreground">{text}</span>
                  </span>
                </li>
              ))}
            </ul>
          </Reveal>
        </div>
        <div>
          <Reveal delay={0.1}>
            <h3 className="text-sm font-semibold">Install the skill</h3>
            <p className="mt-1 text-sm text-muted-foreground">Pick your harness. The skills follow the Agent Skills spec and update with each release.</p>
            <InstallTabs tabs={agentInstall} className="mt-4" />
          </Reveal>
          <Reveal delay={0.2}>
            <h3 className="mt-8 text-sm font-semibold">Read the docs as Markdown</h3>
            <ul className="mt-3 grid gap-2 text-sm sm:grid-cols-2">
              {[
                ['/llms.txt', 'index of every page and skill'],
                ['/llms-full.txt', 'the whole documentation in one file'],
                ['/docs/<page>.md', 'any page, raw'],
                ['/skills/<name>.md', 'any skill, ready to save'],
              ].map(([path, text]) => (
                <li key={path} className="rounded-lg border border-border bg-card px-3 py-2.5">
                  <a href={path.includes('<') ? '/llms.txt' : path} className="font-mono text-[12.5px] text-primary">
                    {path}
                  </a>
                  <p className="mt-0.5 text-xs text-muted-foreground">{text}</p>
                </li>
              ))}
            </ul>
          </Reveal>
        </div>
      </div>
    </section>
  );
}

function Cell({ value }: { value: boolean | string }) {
  if (value === true) return <Check className="size-4 text-ok" aria-label="yes" />;
  if (value === false) return <Minus className="size-4 text-muted-foreground/50" aria-label="no" />;
  return <span className="text-xs text-muted-foreground">{value}</span>;
}

function Compare() {
  return (
    <section className="border-t border-border/70">
      <div className="container-x py-16 md:py-24">
        <SectionHeading
          eyebrow="Why"
          title="Unity's CLI stops at the editor door. UCP is the layer inside."
          blurb="Unity's own CLI covers installs, licensing, project scaffolding and CI plumbing. UCP reaches the scene graph, serialized properties, play mode, the console and the profiler, and the two compose."
        />
        <Reveal delay={0.1} className="mt-10 overflow-x-auto rounded-xl border border-border">
          <table className="w-full min-w-[36rem] text-sm">
            <thead>
              <tr className="bg-muted/60 text-left text-xs uppercase tracking-wider text-muted-foreground">
                <th className="px-4 py-3 font-semibold">Capability</th>
                <th className="w-40 px-4 py-3 font-semibold">Unity CLI</th>
                <th className="w-40 px-4 py-3 font-semibold text-primary">UCP</th>
              </tr>
            </thead>
            <tbody>
              {compare.map((row) => (
                <tr key={row.feature} className="border-t border-border">
                  <td className="px-4 py-2.5">{row.feature}</td>
                  <td className="px-4 py-2.5">
                    <Cell value={row.unity} />
                  </td>
                  <td className="px-4 py-2.5">
                    <Cell value={row.ucp} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Reveal>
      </div>
    </section>
  );
}

function Architecture() {
  const reduce = useReducedMotion();
  const nodes = [
    { title: 'Terminal, script, agent', text: 'Runs ucp. Rust binary, a few milliseconds of overhead per call.' },
    { title: 'ucp CLI', text: 'Discovers the editor through a lock file, authenticates, sends JSON-RPC 2.0 over a localhost WebSocket.' },
    { title: 'Bridge in the editor', text: 'A small package. Executes on the main thread, returns results plus the editor state line.' },
  ];
  return (
    <section className="border-t border-border/70 bg-surface">
      <div className="container-x py-16 md:py-24">
        <SectionHeading
          eyebrow="How it works"
          title="Two parts, one localhost socket."
          blurb="ucp install adds the bridge package to a project. ucp open launches the editor, or attaches to the one already open. Nothing leaves your machine."
        />
        <Reveal delay={0.1} className="mt-10">
          <div className="relative grid gap-4 md:grid-cols-3">
            <svg className="pointer-events-none absolute inset-x-0 top-1/2 hidden h-px w-full md:block" aria-hidden>
              <line x1="0" y1="0.5" x2="100%" y2="0.5" className="stroke-border" strokeWidth="1" />
              {!reduce && <line x1="0" y1="0.5" x2="100%" y2="0.5" className="animate-flow stroke-primary" strokeWidth="1.5" strokeDasharray="6 18" />}
            </svg>
            {nodes.map((node, i) => (
              <div key={node.title} className="relative rounded-xl border border-border bg-card p-5">
                <div className="flex items-center gap-2">
                  <span className="inline-flex size-6 items-center justify-center rounded-full bg-primary/10 font-mono text-[11px] font-semibold text-primary">{i + 1}</span>
                  <h3 className="text-sm font-semibold">{node.title}</h3>
                </div>
                <p className="mt-2 text-sm leading-6 text-muted-foreground">{node.text}</p>
              </div>
            ))}
          </div>
        </Reveal>
      </div>
    </section>
  );
}

function QuickStart() {
  const code = `npm install -g @mflrevan/ucp
cd path/to/UnityProject
ucp install            # adds com.ucp.bridge to the project
ucp open               # launches the editor and waits for the bridge
ucp scene list
ucp object create Crate --primitive Cube
ucp screenshot -o crate.png`;
  return (
    <section className="border-t border-border/70">
      <div className="container-x grid items-center gap-10 py-16 md:py-24 lg:grid-cols-[1fr_1.1fr]">
        <SectionHeading
          eyebrow="Sixty seconds"
          title="From npm to a controlled editor."
          blurb="No account, no license activation, no service. The bridge ships as a local package and the editor you already use does the work."
        />
        <Reveal delay={0.1}>
          <CodeBlock code={code} lang="bash" title="quick start" />
          <div className="mt-4 flex flex-wrap gap-3 text-sm">
            <Link to="/docs/quickstart" className="inline-flex items-center gap-1.5 font-medium text-primary hover:underline">
              <TerminalIcon className="size-4" aria-hidden /> Full quick start
            </Link>
            <Link to="/docs/installation" className="inline-flex items-center gap-1.5 text-muted-foreground hover:text-foreground">
              <FlaskConical className="size-4" aria-hidden /> Installation and compatibility
            </Link>
          </div>
        </Reveal>
      </div>
    </section>
  );
}
