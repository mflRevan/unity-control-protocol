import { Hero } from '@/components/hero';
import { Features } from '@/components/features';
import { Architecture } from '@/components/architecture';
import { QuickStart } from '@/components/quickstart';
import { CTA } from '@/components/cta';
import { DotGrid } from '@/components/dot-grid';

export function LandingPage() {
  return (
    <div className="relative isolate">
      {/* Full-page DotGrid background */}
      <div className="fixed inset-0 z-0 pointer-events-none opacity-90">
        <DotGrid
          dotSize={2}
          gap={29}
          baseColor="#271E37"
          activeColor="#5227FF"
          proximity={150}
          shockRadius={240}
          shockStrength={3}
          resistance={2000}
          returnDuration={1.4}
        />
      </div>

      <div className="relative z-10">
        <Hero />
        <Features />
        <Architecture />
        <QuickStart />
        <CTA />
      </div>
    </div>
  );
}
