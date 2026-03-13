import { Link } from 'react-router-dom';
import { ArrowRight, Github } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { FadeIn, GlowCard } from '@/components/animations';
import { TextType } from '@/components/text-type';

export function CTA() {
  return (
    <section className="px-6 py-20 md:py-24">
      <div className="relative z-10 mx-auto max-w-3xl">
        <FadeIn>
          <GlowCard className="rounded-[28px] border-border/50 bg-card/88 shadow-[0_30px_80px_rgba(0,0,0,0.26)] backdrop-blur-md">
            <div className="relative overflow-hidden px-8 py-16 text-center md:px-16">
              <div className="absolute inset-x-10 top-0 h-px bg-linear-to-r from-transparent via-primary/45 to-transparent" />
              <div className="absolute inset-x-0 top-0 h-28 bg-linear-to-b from-primary/8 to-transparent" />
              <h2 className="text-3xl sm:text-4xl font-bold tracking-tight">
                Ready to automate your <br />
                <span className="bg-linear-to-r from-primary to-purple-400 bg-clip-text text-transparent">
                  <TextType text="Unity workflow" speed={60} delay={400} cursor={false} />
                </span>
                ?
              </h2>
              <p className="mt-4 text-lg text-muted-foreground">
                Install UCP and start controlling Unity Editor in seconds. Open source, MIT licensed.
              </p>
              <div className="mt-8 flex flex-col sm:flex-row gap-3 justify-center">
                <Link to="/docs">
                  <Button size="lg" className="gap-2 group">
                    Read the Docs
                    <ArrowRight className="h-4 w-4 group-hover:translate-x-0.5 transition-transform" />
                  </Button>
                </Link>
                <a href="https://github.com/mflRevan/unity-control-protocol" target="_blank" rel="noopener noreferrer">
                  <Button size="lg" variant="outline" className="gap-2 border-border/60 hover:border-primary/40">
                    <Github className="h-4 w-4" />
                    View Source
                  </Button>
                </a>
              </div>
            </div>
          </GlowCard>
        </FadeIn>
      </div>
    </section>
  );
}
