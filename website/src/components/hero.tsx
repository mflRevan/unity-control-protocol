import { Link } from 'react-router-dom';
import { ArrowRight, Github, Copy, Check } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { AnimatedTerminal } from '@/components/animated-terminal';
import { FadeIn } from '@/components/animations';
import { useState } from 'react';

export function Hero() {
  const [copied, setCopied] = useState(false);

  const handleCopy = () => {
    navigator.clipboard.writeText('npm install -g @mflrevan/ucp');
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <section className="relative min-h-screen flex items-center overflow-hidden">
      <div className="mx-auto max-w-7xl px-6 py-32 w-full relative z-10">
        <div className="grid lg:grid-cols-2 gap-12 lg:gap-16 items-center">
          {/* Left - Content */}
          <div className="space-y-8">
            <FadeIn delay={0.1}>
              <Badge
                variant="secondary"
                className="px-3 py-1.5 text-xs font-medium border border-border bg-muted/40 text-muted-foreground"
              >
                <span className="inline-block w-1.5 h-1.5 rounded-full bg-primary mr-2" />
                Open source · MIT · npm + cargo + binaries
              </Badge>
            </FadeIn>

            <div>
              <h1 className="text-4xl sm:text-5xl lg:text-6xl font-bold tracking-tight leading-[1.05] text-foreground">
                Unity Control Protocol
              </h1>
            </div>

            <FadeIn delay={0.1}>
              <p className="text-lg text-muted-foreground max-w-lg leading-relaxed">
                Drive the Unity Editor from your terminal. Snapshot scene hierarchies, author transforms, enter play
                mode, run tests, capture screenshots, and manage assets, packages, and builds &mdash; over one local
                WebSocket bridge. Scriptable and headless for CI, tooling, and AI agents.
              </p>
            </FadeIn>

            <FadeIn delay={0.25}>
              <div className="space-y-3">
                <div className="flex flex-col sm:flex-row gap-3">
                  <Link to="/docs">
                    <Button size="lg" className="gap-2 group relative overflow-hidden">
                      <span className="relative z-10 flex items-center gap-2">
                        Get Started
                        <ArrowRight className="h-4 w-4 group-hover:translate-x-0.5 transition-transform" />
                      </span>
                    </Button>
                  </Link>
                  <a href="https://github.com/mflRevan/unity-control-protocol" target="_blank" rel="noopener noreferrer">
                    <Button size="lg" variant="outline" className="gap-2 border-border/60 hover:border-primary/40">
                      <Github className="h-4 w-4" />
                      View on GitHub
                    </Button>
                  </a>
                </div>
                <div
                  onClick={handleCopy}
                  className="inline-flex items-center gap-3 px-4 py-2.5 rounded-lg bg-muted/50 border border-border hover:border-primary/30 cursor-pointer transition-all group/copy hover:bg-muted/80"
                >
                  <code className="text-sm font-mono text-muted-foreground">
                    <span className="text-primary/70">$</span> npm install -g @mflrevan/ucp
                  </code>
                  {copied ? (
                    <Check className="h-4 w-4 text-emerald-500" />
                  ) : (
                    <Copy className="h-4 w-4 text-muted-foreground group-hover/copy:text-foreground transition-colors" />
                  )}
                </div>
              </div>
            </FadeIn>
          </div>

          {/* Right - Terminal */}
          <FadeIn delay={0.4} direction="right">
            <div className="lg:ml-8">
              <AnimatedTerminal />
            </div>
          </FadeIn>
        </div>
      </div>
    </section>
  );
}
