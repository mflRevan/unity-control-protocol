import { Hero } from '@/components/hero';
import { Features } from '@/components/features';
import { Architecture } from '@/components/architecture';
import { QuickStart } from '@/components/quickstart';
import { CTA } from '@/components/cta';

export function LandingPage() {
  return (
    <div className="relative isolate">
      <Hero />
      <Features />
      <Architecture />
      <QuickStart />
      <CTA />
    </div>
  );
}
