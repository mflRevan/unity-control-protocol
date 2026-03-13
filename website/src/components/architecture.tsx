import { FadeIn } from '@/components/animations';
import { Terminal, Wifi, Box } from 'lucide-react';

function PulsingDot() {
  return (
    <span className="relative flex h-3 w-3">
      <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-primary/40" />
      <span className="relative inline-flex rounded-full h-3 w-3 bg-primary" />
    </span>
  );
}

function AnimatedArrow() {
  return (
    <div className="hidden md:flex items-center justify-center relative">
      <div className="h-px w-full bg-linear-to-r from-primary/20 via-primary/50 to-primary/20 relative">
        {/* Animated travelling dot */}
        <div className="absolute top-1/2 -translate-y-1/2 w-2 h-2 rounded-full bg-primary shadow-[0_0_8px_var(--color-primary)] animate-[slideRight_3s_ease-in-out_infinite]" />
      </div>
      <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2">
        <PulsingDot />
      </div>
    </div>
  );
}

export function Architecture() {
  return (
    <section className="py-24 relative" id="architecture">
      <div className="mx-auto max-w-6xl px-6">
        <FadeIn>
          <div className="text-center mb-16">
            <p className="text-primary font-medium text-sm tracking-wider uppercase mb-3">Architecture</p>
            <h2 className="text-3xl sm:text-4xl font-bold tracking-tight">
              How it{' '}
              <span className="bg-linear-to-r from-primary to-purple-400 bg-clip-text text-transparent">works</span>
            </h2>
            <p className="mt-4 text-lg text-muted-foreground max-w-2xl mx-auto">
              UCP connects your tools to Unity Editor through a lightweight WebSocket bridge running inside the editor.
            </p>
          </div>
        </FadeIn>

        <FadeIn delay={0.2}>
          <div className="relative rounded-2xl border border-border/50 bg-linear-to-b from-card to-card/50 p-8 md:p-12 overflow-hidden">
            {/* Background glow */}
            <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-125 h-75 bg-primary/5 rounded-full blur-[100px]" />

            <div className="relative grid md:grid-cols-[1fr_auto_1fr_auto_1fr] gap-6 md:gap-4 items-center">
              {/* CLI Side */}
              <div className="text-center space-y-4 group">
                <div className="mx-auto w-20 h-20 rounded-2xl bg-linear-to-br from-primary/15 to-primary/5 border border-primary/20 flex items-center justify-center transition-all group-hover:scale-105 group-hover:border-primary/40 group-hover:shadow-[0_0_20px_var(--color-primary)/0.15]">
                  <Terminal className="h-8 w-8 text-primary" />
                </div>
                <div>
                  <h3 className="font-semibold text-lg">CLI / Agent</h3>
                  <p className="text-sm text-muted-foreground mt-1.5">
                    Rust binary, AI agent, or CI/CD pipeline sends commands
                  </p>
                </div>
                <div className="flex flex-wrap justify-center gap-1.5">
                  {['cargo', 'npm', 'binary'].map((tag) => (
                    <span
                      key={tag}
                      className="px-2 py-0.5 text-xs font-mono rounded-md bg-muted/80 text-muted-foreground border border-border/50"
                    >
                      {tag}
                    </span>
                  ))}
                </div>
              </div>

              {/* Arrow 1 */}
              <AnimatedArrow />

              {/* Connection */}
              <div className="text-center space-y-4 group">
                <div className="mx-auto w-20 h-20 rounded-2xl bg-linear-to-br from-blue-500/15 to-cyan-500/5 border border-blue-500/20 flex items-center justify-center transition-all group-hover:scale-105 group-hover:border-blue-500/40">
                  <Wifi className="h-8 w-8 text-blue-400" />
                </div>
                <div>
                  <h3 className="font-semibold text-lg text-blue-400">WebSocket</h3>
                  <p className="text-sm text-muted-foreground mt-1.5">JSON-RPC 2.0 on localhost with token auth</p>
                </div>
                <span className="inline-block px-2 py-0.5 text-xs font-mono rounded-md bg-muted/80 text-muted-foreground border border-border/50">
                  127.0.0.1:21342
                </span>
              </div>

              {/* Arrow 2 */}
              <AnimatedArrow />

              {/* Unity Side */}
              <div className="text-center space-y-4 group">
                <div className="mx-auto w-20 h-20 rounded-2xl bg-linear-to-br from-emerald-500/15 to-green-500/5 border border-emerald-500/20 flex items-center justify-center transition-all group-hover:scale-105 group-hover:border-emerald-500/40">
                  <Box className="h-8 w-8 text-emerald-400" />
                </div>
                <div>
                  <h3 className="font-semibold text-lg">Unity Editor</h3>
                  <p className="text-sm text-muted-foreground mt-1.5">
                    Bridge package receives commands and controls the editor
                  </p>
                </div>
                <div className="flex flex-wrap justify-center gap-1.5">
                  {['UPM', 'auto-start', 'Unity 2021.3+'].map((tag) => (
                    <span
                      key={tag}
                      className="px-2 py-0.5 text-xs font-mono rounded-md bg-muted/80 text-muted-foreground border border-border/50"
                    >
                      {tag}
                    </span>
                  ))}
                </div>
              </div>
            </div>
          </div>
        </FadeIn>
      </div>
    </section>
  );
}
