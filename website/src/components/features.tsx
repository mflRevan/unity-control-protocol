import { Terminal, Wifi, FolderCode, Gamepad2, TestTube2, GitBranch, FileCode2, Camera, Zap } from 'lucide-react';
import { GlowCard, FadeIn } from '@/components/animations';

const features = [
  {
    icon: Terminal,
    title: 'CLI-First',
    description:
      'Full-featured Rust CLI with human-readable and JSON output modes. Works from any terminal or automation script.',
  },
  {
    icon: Wifi,
    title: 'WebSocket Bridge',
    description: 'JSON-RPC 2.0 over WebSocket. Secure, token-authenticated connection between CLI and Unity Editor.',
  },
  {
    icon: Gamepad2,
    title: 'Play Mode Control',
    description: 'Enter, exit, and pause play mode programmatically. Run tests in edit or play mode with filtering.',
  },
  {
    icon: FolderCode,
    title: 'Scene Management',
    description: 'List, load, and inspect scenes. Capture full hierarchy snapshots with component and property data.',
  },
  {
    icon: FileCode2,
    title: 'File Operations',
    description:
      'Read, write, and patch project files with automatic compilation triggers. Sandboxed to the project directory.',
  },
  {
    icon: Camera,
    title: 'Screenshots & Logs',
    description: 'Capture game or scene view screenshots. Stream Unity console logs in real time with level filtering.',
  },
  {
    icon: GitBranch,
    title: 'Version Control',
    description: 'Full Plastic SCM / Unity VCS integration. Commit, checkout, diff, lock, branch - all from the CLI.',
  },
  {
    icon: TestTube2,
    title: 'Editor Scripting',
    description:
      'Playwright-like script system. Write C# IUCPScript classes and execute them remotely with parameters.',
  },
  {
    icon: Zap,
    title: 'Cross-Platform',
    description: 'macOS (x64 + ARM), Linux, and Windows. Install via cargo, npm, or grab a prebuilt binary.',
  },
];

export function Features() {
  return (
    <section className="py-24 relative" id="features">
      <div className="mx-auto max-w-7xl px-6">
        <FadeIn>
          <div className="text-center mb-16">
            <p className="text-primary font-medium text-sm tracking-wider uppercase mb-3">Capabilities</p>
            <h2 className="text-3xl sm:text-4xl font-bold tracking-tight text-foreground">
              One bridge for the whole <span className="text-primary">editor</span>
            </h2>
            <p className="mt-4 text-lg text-muted-foreground max-w-2xl mx-auto">
              Inspect scenes, author objects and transforms, drive play mode and tests, patch project files, and manage
              version control &mdash; all from the command line.
            </p>
          </div>
        </FadeIn>

        <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-5">
          {features.map((feature) => (
            <GlowCard key={feature.title} className="h-full">
              <div className="p-6 space-y-3">
                <div className="inline-flex items-center justify-center w-10 h-10 rounded-lg border border-primary/20 bg-primary/5">
                  <feature.icon className="h-5 w-5 text-primary" />
                </div>
                <h3 className="font-semibold text-lg text-foreground">{feature.title}</h3>
                <p className="text-sm text-muted-foreground leading-relaxed">{feature.description}</p>
              </div>
            </GlowCard>
          ))}
        </div>
      </div>
    </section>
  );
}
