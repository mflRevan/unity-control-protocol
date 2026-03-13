import { useEffect, useRef } from 'react';
import { useTheme } from '@/components/theme-provider';

export function HexGrid() {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const { resolved } = useTheme();

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    let animId: number;
    let time = 0;

    const resize = () => {
      const dpr = window.devicePixelRatio || 1;
      canvas.width = canvas.offsetWidth * dpr;
      canvas.height = canvas.offsetHeight * dpr;
      ctx.scale(dpr, dpr);
    };

    resize();
    window.addEventListener('resize', resize);

    const hexSize = 35;
    const hexH = hexSize * Math.sqrt(3);
    const hexW = hexSize * 2;

    // Pre-compute hex vertices for performance
    const hexAngles = Array.from({ length: 6 }, (_, i) => ({
      cos: Math.cos((Math.PI / 3) * i - Math.PI / 6),
      sin: Math.sin((Math.PI / 3) * i - Math.PI / 6),
    }));

    function drawHex(cx: number, cy: number, alpha: number, glow: boolean, intensity: number) {
      if (!ctx) return;
      ctx.beginPath();
      for (let i = 0; i < 6; i++) {
        const x = cx + hexSize * hexAngles[i].cos;
        const y = cy + hexSize * hexAngles[i].sin;
        if (i === 0) ctx.moveTo(x, y);
        else ctx.lineTo(x, y);
      }
      ctx.closePath();

      const isDark = resolved === 'dark';

      if (glow) {
        const glowAlpha = isDark ? alpha * 0.2 * intensity : alpha * 0.1 * intensity;
        ctx.fillStyle = isDark ? `rgba(167, 139, 250, ${glowAlpha})` : `rgba(109, 40, 217, ${glowAlpha})`;
        ctx.fill();
      }

      const strokeAlpha = isDark ? alpha * 0.18 : alpha * 0.1;
      ctx.strokeStyle = isDark ? `rgba(167, 139, 250, ${strokeAlpha})` : `rgba(109, 40, 217, ${strokeAlpha})`;
      ctx.lineWidth = intensity > 0.7 ? 1 : 0.5;
      ctx.stroke();
    }

    // Track "energy beams" that travel along hex paths
    const beamCount = 5;
    const beams = Array.from({ length: beamCount }, (_, i) => ({
      angle: (i / beamCount) * Math.PI * 2,
      speed: 0.3 + Math.random() * 0.4,
      radius: 0.15 + Math.random() * 0.2,
      phase: Math.random() * Math.PI * 2,
    }));

    function animate() {
      if (!ctx || !canvas) return;
      time += 0.004;

      const w = canvas.offsetWidth;
      const h = canvas.offsetHeight;
      ctx.clearRect(0, 0, w, h);

      const cols = Math.ceil(w / (hexW * 0.75)) + 2;
      const rows = Math.ceil(h / hexH) + 2;
      const isDark = resolved === 'dark';

      // Calculate beam positions
      const beamPositions = beams.map((b) => ({
        x: w * 0.5 + Math.cos(time * b.speed + b.angle) * w * b.radius,
        y: h * 0.4 + Math.sin(time * b.speed * 0.7 + b.phase) * h * b.radius,
      }));

      for (let row = -1; row < rows; row++) {
        for (let col = -1; col < cols; col++) {
          const cx = col * hexW * 0.75;
          const cy = row * hexH + (col % 2 === 0 ? 0 : hexH / 2);

          // Distance from center for radial fade
          const dx = (cx - w / 2) / w;
          const dy = (cy - h * 0.4) / h;
          const dist = Math.sqrt(dx * dx + dy * dy);

          const radialFade = Math.max(0, 1 - dist * 1.6);

          // Multiple overlapping waves for complex patterns
          const wave1 = Math.sin(time * 1.5 + cx * 0.01 + cy * 0.008) * 0.5 + 0.5;
          const wave2 = Math.sin(time * 2.5 - cx * 0.006 + cy * 0.012) * 0.5 + 0.5;
          const wave3 = Math.sin(time * 0.8 + (cx + cy) * 0.005) * 0.5 + 0.5;

          // Check if near any beam
          let beamInfluence = 0;
          for (const bp of beamPositions) {
            const bdx = cx - bp.x;
            const bdy = cy - bp.y;
            const bdist = Math.sqrt(bdx * bdx + bdy * bdy);
            beamInfluence = Math.max(beamInfluence, Math.max(0, 1 - bdist / 120));
          }

          const combined = wave1 * 0.3 + wave2 * 0.3 + wave3 * 0.2 + beamInfluence * 0.8;
          const alpha = radialFade * (0.15 + combined * 0.85);
          const glow = (combined > 0.6 && radialFade > 0.2) || beamInfluence > 0.3;

          if (alpha > 0.02) {
            drawHex(cx, cy, alpha, glow, combined);
          }
        }
      }

      // Floating particles with trails
      const particleCount = 12;
      for (let i = 0; i < particleCount; i++) {
        const t = time * 0.6 + (i * Math.PI * 2) / particleCount;
        const orbitX = Math.cos(t * 0.5 + i * 1.1) * w * (0.15 + (i % 3) * 0.1);
        const orbitY = Math.sin(t * 0.3 + i * 0.8) * h * (0.12 + (i % 4) * 0.06);
        const px = w * 0.5 + orbitX;
        const py = h * 0.4 + orbitY;
        const pAlpha = (Math.sin(t * 1.5 + i) * 0.5 + 0.5) * 0.8;

        // Particle trail (3 trailing dots)
        for (let trail = 0; trail < 3; trail++) {
          const tt = t - trail * 0.08;
          const tpx = w * 0.5 + Math.cos(tt * 0.5 + i * 1.1) * w * (0.15 + (i % 3) * 0.1);
          const tpy = h * 0.4 + Math.sin(tt * 0.3 + i * 0.8) * h * (0.12 + (i % 4) * 0.06);
          const ta = pAlpha * (1 - trail * 0.35);
          if (ta > 0.05) {
            ctx.beginPath();
            ctx.arc(tpx, tpy, 1.2 - trail * 0.3, 0, Math.PI * 2);
            ctx.fillStyle = isDark ? `rgba(167, 139, 250, ${ta * 0.4})` : `rgba(109, 40, 217, ${ta * 0.25})`;
            ctx.fill();
          }
        }

        // Main particle
        ctx.beginPath();
        ctx.arc(px, py, 2, 0, Math.PI * 2);
        ctx.fillStyle = isDark ? `rgba(167, 139, 250, ${pAlpha * 0.6})` : `rgba(109, 40, 217, ${pAlpha * 0.4})`;
        ctx.fill();

        // Glow halo
        ctx.beginPath();
        ctx.arc(px, py, 8, 0, Math.PI * 2);
        ctx.fillStyle = isDark ? `rgba(167, 139, 250, ${pAlpha * 0.06})` : `rgba(109, 40, 217, ${pAlpha * 0.03})`;
        ctx.fill();
      }

      animId = requestAnimationFrame(animate);
    }

    animate();

    return () => {
      cancelAnimationFrame(animId);
      window.removeEventListener('resize', resize);
    };
  }, [resolved]);

  return <canvas ref={canvasRef} className="absolute inset-0 w-full h-full pointer-events-none" />;
}
