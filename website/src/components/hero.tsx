import { Link } from 'react-router-dom';
import { ArrowRight, Github, Copy, Check } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { AnimatedTerminal } from '@/components/animated-terminal';
import { FadeIn } from '@/components/animations';
import { TextType } from '@/components/text-type';
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
      {/* Subtle gradient blurs for depth */}
      <div className="absolute inset-0 -z-10 pointer-events-none">
        <div className="absolute top-1/4 left-1/2 -translate-x-1/2 -translate-y-1/4 w-200 h-150 bg-primary/10 rounded-full blur-[150px]" />
        <div className="absolute bottom-0 right-0 w-125 h-100 bg-primary/5 rounded-full blur-[120px]" />
        <div className="absolute top-0 left-0 w-75 h-75 bg-primary/5 rounded-full blur-[100px]" />
      </div>

      {/* Gradient overlay at bottom for smooth section transition */}
      <div className="absolute bottom-0 left-0 right-0 h-32 bg-linear-to-t from-background to-transparent z-0" />

      <div className="mx-auto max-w-7xl px-6 py-32 w-full relative z-10">
        <div className="grid lg:grid-cols-2 gap-12 lg:gap-16 items-center">
          {/* Left - Content */}
          <div className="space-y-8">
            <FadeIn delay={0.1}>
              <Badge
                variant="secondary"
                className="px-3 py-1.5 text-xs font-medium border border-primary/20 bg-primary/5 text-primary"
              >
                <span className="inline-block w-1.5 h-1.5 rounded-full bg-emerald-400 mr-2 animate-pulse" />
                Now available on npm
              </Badge>
            </FadeIn>

            <div>
              <h1 className="text-4xl sm:text-5xl lg:text-6xl xl:text-7xl font-bold tracking-tight leading-[1.05]">
                <TextType text="Unity Control" speed={70} delay={300} cursor={true} />
                <br />
                <span className="bg-linear-to-r from-primary via-purple-400 to-primary bg-clip-text text-transparent">
                  <TextType text="Protocol" speed={70} delay={1300} />
                </span>
              </h1>
            </div>

            <FadeIn delay={0.6}>
              <p className="text-lg text-muted-foreground max-w-lg leading-relaxed">
                A cross-platform CLI + Unity Editor bridge for programmatic control. Enable AI agents, CI/CD pipelines,
                and automation tools to interact with Unity over WebSocket.
              </p>
            </FadeIn>

            <FadeIn delay={0.8}>
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
