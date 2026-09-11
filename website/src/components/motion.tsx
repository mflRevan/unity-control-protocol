import { motion, useReducedMotion } from 'framer-motion';
import type { ComponentProps, ReactNode } from 'react';
import { ease, fadeUp, stagger } from '@/lib/motion-presets';

type RevealProps = ComponentProps<typeof motion.div> & {
  children: ReactNode;
  /** Delay in seconds before the reveal starts. */
  delay?: number;
  /** Reveal once when 20% of the element is in view (default) or immediately on mount. */
  mode?: 'in-view' | 'mount';
};

/** Fades and lifts content into place. Renders statically when the user prefers reduced motion. */
export function Reveal({ children, delay = 0, mode = 'in-view', ...rest }: RevealProps) {
  const reduce = useReducedMotion();
  if (reduce) return <div className={rest.className}>{children}</div>;
  const animate = mode === 'mount' ? { animate: 'visible' } : { whileInView: 'visible', viewport: { once: true, amount: 0.2 } };
  return (
    <motion.div
      initial="hidden"
      variants={{
        hidden: fadeUp.hidden,
        visible: { opacity: 1, y: 0, transition: { duration: 0.55, ease, delay } },
      }}
      {...animate}
      {...rest}
    >
      {children}
    </motion.div>
  );
}

type StaggerProps = ComponentProps<typeof motion.div> & { children: ReactNode };

/** Container whose direct `StaggerItem` children reveal one after another. */
export function Stagger({ children, ...rest }: StaggerProps) {
  const reduce = useReducedMotion();
  if (reduce) return <div className={rest.className}>{children}</div>;
  return (
    <motion.div initial="hidden" whileInView="visible" viewport={{ once: true, amount: 0.15 }} variants={stagger} {...rest}>
      {children}
    </motion.div>
  );
}

export function StaggerItem({ children, ...rest }: StaggerProps) {
  const reduce = useReducedMotion();
  if (reduce) return <div className={rest.className}>{children}</div>;
  return (
    <motion.div variants={fadeUp} {...rest}>
      {children}
    </motion.div>
  );
}

/** Route-level enter transition. */
export function PageTransition({ children }: { children: ReactNode }) {
  const reduce = useReducedMotion();
  if (reduce) return <>{children}</>;
  return (
    <motion.div initial={{ opacity: 0, y: 6 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.28, ease }}>
      {children}
    </motion.div>
  );
}
