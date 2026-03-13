import { Link } from 'react-router-dom';
import { ArrowRight, Github } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { FadeIn, GlowCard } from '@/components/animations';
import { Prism } from '@/components/prism';
import { TextType } from '@/components/text-type';

export function CTA() {
  return (
    <section className="py-24 relative overflow-hidden">
      {/* Prism background behind the whole CTA section */}
      <div className="absolute inset-0 -z-10">
        <Prism />
      </div>

      <div className="mx-auto max-w-3xl px-6">
        <FadeIn>
          <GlowCard className="rounded-2xl">
            <div className="relative text-center px-8 py-16 md:px-16 overflow-hidden">
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
