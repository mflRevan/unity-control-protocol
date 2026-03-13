import { Hero } from '@/components/hero';
import { Features } from '@/components/features';
import { Architecture } from '@/components/architecture';
import { QuickStart } from '@/components/quickstart';
import { CTA } from '@/components/cta';
import { DotGrid } from '@/components/dot-grid';
import { useTheme } from '@/components/theme-provider';

export function LandingPage() {
  const { resolved } = useTheme();
  const isDark = resolved === 'dark';

  return (
    <div className="relative isolate">
      {/* Full-page DotGrid background */}
      <div
        className="fixed inset-0 z-0 pointer-events-none transition-opacity duration-500"
        style={{
          opacity: isDark ? 0.9 : 0.22,
          maskImage: 'linear-gradient(to bottom, rgba(0,0,0,0.4), rgba(0,0,0,0.14) 40%, rgba(0,0,0,0))',
        }}
      >
        <DotGrid
          dotSize={2}
          gap={29}
          baseColor={isDark ? '#271E37' : '#DDD4F0'}
          activeColor={isDark ? '#5227FF' : '#8A63FF'}
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
