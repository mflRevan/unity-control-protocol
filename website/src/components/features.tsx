import { Terminal, Wifi, FolderCode, Gamepad2, TestTube2, GitBranch, FileCode2, Camera, Zap } from 'lucide-react';
import { GlowCard, FadeIn } from '@/components/animations';

const features = [
  {
    icon: Terminal,
    title: 'CLI-First',
    description:
      'Full-featured Rust CLI with human-readable and JSON output modes. Works from any terminal or automation script.',
    accent: 'from-violet-500/20 to-purple-500/20',
  },
  {
    icon: Wifi,
    title: 'WebSocket Bridge',
    description: 'JSON-RPC 2.0 over WebSocket. Secure, token-authenticated connection between CLI and Unity Editor.',
    accent: 'from-blue-500/20 to-cyan-500/20',
  },
  {
    icon: Gamepad2,
    title: 'Play Mode Control',
    description: 'Enter, exit, and pause play mode programmatically. Run tests in edit or play mode with filtering.',
    accent: 'from-emerald-500/20 to-green-500/20',
  },
  {
    icon: FolderCode,
    title: 'Scene Management',
    description: 'List, load, and inspect scenes. Capture full hierarchy snapshots with component and property data.',
    accent: 'from-orange-500/20 to-amber-500/20',
  },
  {
    icon: FileCode2,
    title: 'File Operations',
    description:
      'Read, write, and patch project files with automatic compilation triggers. Sandboxed to the project directory.',
    accent: 'from-pink-500/20 to-rose-500/20',
  },
  {
    icon: Camera,
    title: 'Screenshots & Logs',
    description: 'Capture game or scene view screenshots. Stream Unity console logs in real time with level filtering.',
    accent: 'from-sky-500/20 to-blue-500/20',
  },
  {
    icon: GitBranch,
    title: 'Version Control',
    description: 'Full Plastic SCM / Unity VCS integration. Commit, checkout, diff, lock, branch - all from the CLI.',
    accent: 'from-teal-500/20 to-emerald-500/20',
  },
  {
    icon: TestTube2,
    title: 'Editor Scripting',
    description:
      'Playwright-like script system. Write C# IUCPScript classes and execute them remotely with parameters.',
    accent: 'from-fuchsia-500/20 to-purple-500/20',
  },
  {
    icon: Zap,
    title: 'Cross-Platform',
    description: 'macOS (x64 + ARM), Linux, and Windows. Install via cargo, npm, or grab a prebuilt binary.',
    accent: 'from-yellow-500/20 to-orange-500/20',
  },
];

export function Features() {
  return (
    <section className="py-24 relative" id="features">
      {/* Subtle background accents */}
      <div className="absolute inset-0 -z-10 overflow-hidden">
        <div className="absolute top-1/2 left-1/4 w-100 h-100 bg-primary/3 rounded-full blur-[150px]" />
        <div className="absolute bottom-0 right-1/4 w-75 h-75 bg-primary/3 rounded-full blur-[120px]" />
      </div>

      <div className="mx-auto max-w-7xl px-6">
        <FadeIn>
          <div className="text-center mb-16">
            <p className="text-primary font-medium text-sm tracking-wider uppercase mb-3">Features</p>
            <h2 className="text-3xl sm:text-4xl font-bold tracking-tight">
              Everything you need to{' '}
              <span className="bg-linear-to-r from-primary to-purple-400 bg-clip-text text-transparent">
                automate Unity
              </span>
            </h2>
            <p className="mt-4 text-lg text-muted-foreground max-w-2xl mx-auto">
              A comprehensive toolkit for programmatic Unity Editor control - from file operations to play mode to
              version control.
            </p>
          </div>
        </FadeIn>

        <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-5">
          {features.map((feature, i) => (
            <FadeIn key={feature.title} delay={0.05 * i}>
              <GlowCard className="h-full group/card">
                <div className={`h-[2px] bg-linear-to-r ${feature.accent} opacity-60`} />
                <div className="p-6 pt-5 space-y-3">
                  <div
                    className={`inline-flex items-center justify-center w-10 h-10 rounded-lg bg-linear-to-br ${feature.accent} transition-transform duration-300 group-hover/card:scale-110`}
                  >
                    <feature.icon className="h-5 w-5 text-primary" />
                  </div>
                  <h3 className="font-semibold text-lg">{feature.title}</h3>
                  <p className="text-sm text-muted-foreground leading-relaxed">{feature.description}</p>
                </div>
              </GlowCard>
            </FadeIn>
          ))}
        </div>
      </div>
    </section>
  );
}
