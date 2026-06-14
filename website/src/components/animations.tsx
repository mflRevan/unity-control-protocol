import { useState, useEffect, useRef } from 'react';
import { motion, useInView } from 'framer-motion';

interface SplitTextProps {
  text: string;
  className?: string;
  delay?: number;
}

export function SplitText({ text, className = '', delay = 0 }: SplitTextProps) {
  const ref = useRef<HTMLSpanElement>(null);
  const isInView = useInView(ref, { once: true });
  const words = text.split(' ');

  return (
    <span ref={ref} className={className}>
      {words.map((word, i) => (
        <motion.span
          key={i}
          className="inline-block"
          initial={{ opacity: 0, y: 8 }}
          animate={isInView ? { opacity: 1, y: 0 } : {}}
          transition={{
            duration: 0.35,
            delay: delay + i * 0.04,
            ease: [0.25, 0.4, 0.25, 1],
          }}
        >
          {word}&nbsp;
        </motion.span>
      ))}
    </span>
  );
}

interface FadeInProps {
  children: React.ReactNode;
  className?: string;
  delay?: number;
  direction?: 'up' | 'down' | 'left' | 'right';
}

export function FadeIn({ children, className = '', delay = 0 }: FadeInProps) {
  // Content is always rendered and visible — no opacity gating behind scroll, so nothing is
  // hidden from a full render, a JS crawler, or a static capture. A single subtle one-shot
  // entrance plays on mount without ever blocking content.
  void delay;
  return (
    <motion.div
      className={className}
      initial={{ opacity: 1, y: 0 }}
      animate={{ opacity: 1, y: 0 }}
    >
      {children}
    </motion.div>
  );
}

interface CountUpProps {
  target: number;
  suffix?: string;
  prefix?: string;
  className?: string;
}

export function CountUp({ target, suffix = '', prefix = '', className = '' }: CountUpProps) {
  const [count, setCount] = useState(0);
  const ref = useRef<HTMLSpanElement>(null);
  const isInView = useInView(ref, { once: true });

  useEffect(() => {
    if (!isInView) return;
    const duration = 1500;
    const steps = 60;
    const increment = target / steps;
    let current = 0;
    const interval = setInterval(() => {
      current += increment;
      if (current >= target) {
        setCount(target);
        clearInterval(interval);
      } else {
        setCount(Math.floor(current));
      }
    }, duration / steps);
    return () => clearInterval(interval);
  }, [isInView, target]);

  return (
    <span ref={ref} className={className}>
      {prefix}
      {count}
      {suffix}
    </span>
  );
}

interface GlowCardProps {
  children: React.ReactNode;
  className?: string;
}

export function GlowCard({ children, className = '' }: GlowCardProps) {
  return (
    <div
      className={`relative overflow-hidden rounded-xl border border-border bg-card transition-colors duration-200 hover:border-primary/40 ${className}`}
    >
      <div className="relative z-10">{children}</div>
    </div>
  );
}
