import { Link } from 'react-router-dom';
import { ArrowRight, Github } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { FadeIn, GlowCard } from '@/components/animations';
import { TextType } from '@/components/text-type';

function DiscordIcon({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" className={className} fill="currentColor" aria-hidden="true">
      <path d="M18.8943 4.34399C17.5183 3.71467 16.057 3.256 14.5317 3C14.3396 3.33067 14.1263 3.77866 13.977 4.13067C12.3546 3.89599 10.7439 3.89599 9.14391 4.13067C8.99457 3.77866 8.77056 3.33067 8.58922 3C7.05325 3.256 5.59191 3.71467 4.22552 4.34399C1.46286 8.41865 0.716188 12.3973 1.08952 16.3226C2.92418 17.6559 4.69486 18.4666 6.4346 19C6.86126 18.424 7.24527 17.8053 7.57594 17.1546C6.9466 16.92 6.34927 16.632 5.77327 16.2906C5.9226 16.184 6.07194 16.0667 6.21061 15.9493C9.68793 17.5387 13.4543 17.5387 16.889 15.9493C17.0383 16.0667 17.177 16.184 17.3263 16.2906C16.7503 16.632 16.153 16.92 15.5236 17.1546C15.8543 17.8053 16.2383 18.424 16.665 19C18.4036 18.4666 20.185 17.6559 22.01 16.3226C22.4687 11.7787 21.2836 7.83202 18.8943 4.34399ZM8.05593 13.9013C7.01058 13.9013 6.15725 12.952 6.15725 11.7893C6.15725 10.6267 6.98925 9.67731 8.05593 9.67731C9.11191 9.67731 9.97588 10.6267 9.95454 11.7893C9.95454 12.952 9.11191 13.9013 8.05593 13.9013ZM15.065 13.9013C14.0196 13.9013 13.1652 12.952 13.1652 11.7893C13.1652 10.6267 13.9983 9.67731 15.065 9.67731C16.121 9.67731 16.985 10.6267 16.9636 11.7893C16.9636 12.952 16.1317 13.9013 15.065 13.9013Z" />
    </svg>
  );
}

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
                <a href="https://discord.gg/F4RjhdVTbz" target="_blank" rel="noopener noreferrer">
                  <Button
                    size="lg"
                    className="gap-2 border border-[#5865F2]/70 bg-[#5865F2] text-white hover:bg-[#4752C4] hover:border-[#4752C4]"
                  >
                    <DiscordIcon className="h-5 w-5" />
                    Join Discord
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
