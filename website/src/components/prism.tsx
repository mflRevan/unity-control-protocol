import { useEffect, useRef } from 'react';

interface PrismProps {
  className?: string;
}

export function Prism({ className = '' }: PrismProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);

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

    function animate() {
      if (!ctx || !canvas) return;
      time += 0.003;

      const w = canvas.offsetWidth;
      const h = canvas.offsetHeight;
      ctx.clearRect(0, 0, w, h);

      // Create prismatic light beams emanating from center-top
      const centerX = w / 2;
      const sourceY = h * 0.1;

      // Multiple light rays with different colors
      const rays = [
        { color: [82, 39, 255], angle: -0.3, width: 0.15 }, // deep purple
        { color: [120, 80, 255], angle: -0.15, width: 0.12 }, // purple
        { color: [60, 180, 220], angle: 0, width: 0.18 }, // cyan
        { color: [100, 200, 255], angle: 0.1, width: 0.14 }, // light blue
        { color: [167, 139, 250], angle: -0.05, width: 0.16 }, // violet
        { color: [40, 140, 200], angle: 0.2, width: 0.1 }, // blue
        { color: [200, 180, 150], angle: -0.08, width: 0.08 }, // warm
      ];

      for (const ray of rays) {
        const oscillation = Math.sin(time * 1.2 + ray.angle * 5) * 0.04;
        const angle = ray.angle + oscillation;
        const spread = ray.width + Math.sin(time * 0.8 + ray.angle * 3) * 0.03;

        // Create gradient from source downward
        const grad = ctx.createRadialGradient(
          centerX + angle * w * 0.5,
          sourceY,
          0,
          centerX + angle * w * 0.8,
          h * 0.85,
          h * 0.9,
        );

        const [r, g, b] = ray.color;
        const intensity = 0.06 + Math.sin(time + ray.angle * 4) * 0.02;
        grad.addColorStop(0, `rgba(${r}, ${g}, ${b}, ${intensity * 1.5})`);
        grad.addColorStop(0.4, `rgba(${r}, ${g}, ${b}, ${intensity})`);
        grad.addColorStop(0.8, `rgba(${r}, ${g}, ${b}, ${intensity * 0.3})`);
        grad.addColorStop(1, `rgba(${r}, ${g}, ${b}, 0)`);

        ctx.beginPath();
        // Fan shape from source
        const leftAngle = angle - spread;
        const rightAngle = angle + spread;
        ctx.moveTo(centerX, sourceY);
        ctx.lineTo(centerX + leftAngle * w * 1.2, h);
        ctx.lineTo(centerX + rightAngle * w * 1.2, h);
        ctx.closePath();

        ctx.fillStyle = grad;
        ctx.fill();
      }

      // Bright horizontal glow line near bottom
      const lineY = h * 0.88;
      const lineGrad = ctx.createLinearGradient(0, lineY - 20, 0, lineY + 20);
      const lineIntensity = 0.15 + Math.sin(time * 2) * 0.05;
      lineGrad.addColorStop(0, 'rgba(100, 200, 255, 0)');
      lineGrad.addColorStop(0.4, `rgba(100, 200, 255, ${lineIntensity * 0.5})`);
      lineGrad.addColorStop(0.5, `rgba(100, 200, 255, ${lineIntensity})`);
      lineGrad.addColorStop(0.6, `rgba(100, 200, 255, ${lineIntensity * 0.5})`);
      lineGrad.addColorStop(1, 'rgba(100, 200, 255, 0)');

      const hLineGrad = ctx.createLinearGradient(w * 0.1, 0, w * 0.9, 0);
      hLineGrad.addColorStop(0, 'rgba(100, 200, 255, 0)');
      hLineGrad.addColorStop(0.3, `rgba(100, 200, 255, ${lineIntensity})`);
      hLineGrad.addColorStop(0.5, `rgba(120, 220, 255, ${lineIntensity * 1.5})`);
      hLineGrad.addColorStop(0.7, `rgba(100, 200, 255, ${lineIntensity})`);
      hLineGrad.addColorStop(1, 'rgba(100, 200, 255, 0)');

      ctx.fillStyle = hLineGrad;
      ctx.fillRect(0, lineY - 2, w, 4);

      // Soft glow above the line
      const glowGrad = ctx.createRadialGradient(centerX, lineY, 0, centerX, lineY, w * 0.4);
      glowGrad.addColorStop(0, `rgba(82, 39, 255, ${lineIntensity * 0.3})`);
      glowGrad.addColorStop(0.5, `rgba(100, 180, 255, ${lineIntensity * 0.1})`);
      glowGrad.addColorStop(1, 'rgba(100, 180, 255, 0)');
      ctx.fillStyle = glowGrad;
      ctx.fillRect(0, lineY - h * 0.3, w, h * 0.35);

      animId = requestAnimationFrame(animate);
    }

    animId = requestAnimationFrame(animate);

    return () => {
      window.removeEventListener('resize', resize);
      cancelAnimationFrame(animId);
    };
  }, []);

  return <canvas ref={canvasRef} className={`absolute inset-0 w-full h-full pointer-events-none ${className}`} />;
}
