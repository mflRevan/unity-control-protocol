import { useEffect, useRef, useCallback } from 'react';

interface DotGridProps {
  dotSize?: number;
  gap?: number;
  baseColor?: string;
  activeColor?: string;
  proximity?: number;
  shockRadius?: number;
  shockStrength?: number;
  resistance?: number;
  returnDuration?: number;
  className?: string;
}

interface Dot {
  baseX: number;
  baseY: number;
  x: number;
  y: number;
  vx: number;
  vy: number;
  alpha: number;
}

export function DotGrid({
  dotSize = 2,
  gap = 29,
  baseColor = '#271E37',
  activeColor = '#5227FF',
  proximity = 150,
  shockRadius = 240,
  shockStrength = 3,
  resistance = 2000,
  returnDuration = 1.4,
  className = '',
}: DotGridProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const dotsRef = useRef<Dot[]>([]);
  const mouseRef = useRef({ x: -1000, y: -1000 });
  const animFrameRef = useRef<number>(0);
  const lastClickRef = useRef({ x: -1000, y: -1000, time: 0 });

  const initDots = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const w = canvas.offsetWidth;
    const h = canvas.offsetHeight;
    const dots: Dot[] = [];
    const cols = Math.ceil(w / gap) + 1;
    const rows = Math.ceil(h / gap) + 1;
    const offsetX = (w - (cols - 1) * gap) / 2;
    const offsetY = (h - (rows - 1) * gap) / 2;

    for (let r = 0; r < rows; r++) {
      for (let c = 0; c < cols; c++) {
        const x = offsetX + c * gap;
        const y = offsetY + r * gap;
        dots.push({ baseX: x, baseY: y, x, y, vx: 0, vy: 0, alpha: 0 });
      }
    }
    dotsRef.current = dots;
  }, [gap]);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const resize = () => {
      const dpr = window.devicePixelRatio || 1;
      canvas.width = canvas.offsetWidth * dpr;
      canvas.height = canvas.offsetHeight * dpr;
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      initDots();
    };

    resize();
    window.addEventListener('resize', resize);

    const handleMouseMove = (e: MouseEvent) => {
      const rect = canvas.getBoundingClientRect();
      mouseRef.current = { x: e.clientX - rect.left, y: e.clientY - rect.top };
    };

    const handleClick = (e: MouseEvent) => {
      const rect = canvas.getBoundingClientRect();
      lastClickRef.current = {
        x: e.clientX - rect.left,
        y: e.clientY - rect.top,
        time: performance.now(),
      };
    };

    const handleMouseLeave = () => {
      mouseRef.current = { x: -1000, y: -1000 };
    };

    canvas.addEventListener('mousemove', handleMouseMove);
    canvas.addEventListener('click', handleClick);
    canvas.addEventListener('mouseleave', handleMouseLeave);

    const springK = 1 / returnDuration;
    const damping = 0.85;

    function hexToRgb(hex: string) {
      const r = parseInt(hex.slice(1, 3), 16);
      const g = parseInt(hex.slice(3, 5), 16);
      const b = parseInt(hex.slice(5, 7), 16);
      return { r, g, b };
    }

    const baseRgb = hexToRgb(baseColor);
    const activeRgb = hexToRgb(activeColor);

    function animate() {
      if (!ctx || !canvas) return;
      const w = canvas.offsetWidth;
      const h = canvas.offsetHeight;
      ctx.clearRect(0, 0, w, h);

      const mx = mouseRef.current.x;
      const my = mouseRef.current.y;
      const now = performance.now();
      const clickAge = (now - lastClickRef.current.time) / 1000;
      const clickActive = clickAge < 1.5;

      for (const dot of dotsRef.current) {
        // Mouse proximity
        const dxm = mx - dot.baseX;
        const dym = my - dot.baseY;
        const distMouse = Math.sqrt(dxm * dxm + dym * dym);
        const proximityFactor = Math.max(0, 1 - distMouse / proximity);

        // Shock wave from click
        let shockForceX = 0;
        let shockForceY = 0;
        if (clickActive) {
          const dxc = dot.baseX - lastClickRef.current.x;
          const dyc = dot.baseY - lastClickRef.current.y;
          const distClick = Math.sqrt(dxc * dxc + dyc * dyc);
          const wavePos = clickAge * resistance;
          const waveDist = Math.abs(distClick - wavePos);
          if (waveDist < shockRadius && distClick > 0) {
            const shockFactor = (1 - waveDist / shockRadius) * Math.max(0, 1 - clickAge / 1.5);
            shockForceX = (dxc / distClick) * shockStrength * shockFactor;
            shockForceY = (dyc / distClick) * shockStrength * shockFactor;
          }
        }

        // Push dots away from mouse
        let pushX = 0;
        let pushY = 0;
        if (proximityFactor > 0 && distMouse > 0) {
          const pushStrength = proximityFactor * 8;
          pushX = -(dxm / distMouse) * pushStrength;
          pushY = -(dym / distMouse) * pushStrength;
        }

        // Spring back to base position
        const dxb = dot.baseX - dot.x;
        const dyb = dot.baseY - dot.y;
        dot.vx += (dxb * springK + pushX + shockForceX) * 0.16;
        dot.vy += (dyb * springK + pushY + shockForceY) * 0.16;
        dot.vx *= damping;
        dot.vy *= damping;
        dot.x += dot.vx;
        dot.y += dot.vy;

        // Alpha based on proximity and displacement
        const displacement = Math.sqrt((dot.x - dot.baseX) ** 2 + (dot.y - dot.baseY) ** 2);
        dot.alpha = Math.min(1, proximityFactor + displacement * 0.1);

        // Fade out towards left/right edges
        const edgeFadeX = Math.min(dot.baseX / (w * 0.15), (w - dot.baseX) / (w * 0.15), 1);
        const finalAlpha = Math.max(0, edgeFadeX);

        // Interpolate color
        const r = Math.round(baseRgb.r + (activeRgb.r - baseRgb.r) * dot.alpha);
        const g = Math.round(baseRgb.g + (activeRgb.g - baseRgb.g) * dot.alpha);
        const b = Math.round(baseRgb.b + (activeRgb.b - baseRgb.b) * dot.alpha);
        const a = 0.75 + dot.alpha * 0.25;

        ctx.beginPath();
        ctx.arc(dot.x, dot.y, dotSize, 0, Math.PI * 2);
        ctx.fillStyle = `rgba(${r}, ${g}, ${b}, ${Math.min(1, finalAlpha * a)})`;
        ctx.fill();
      }

      animFrameRef.current = requestAnimationFrame(animate);
    }

    animFrameRef.current = requestAnimationFrame(animate);

    return () => {
      window.removeEventListener('resize', resize);
      canvas.removeEventListener('mousemove', handleMouseMove);
      canvas.removeEventListener('click', handleClick);
      canvas.removeEventListener('mouseleave', handleMouseLeave);
      cancelAnimationFrame(animFrameRef.current);
    };
  }, [dotSize, baseColor, activeColor, proximity, shockRadius, shockStrength, resistance, returnDuration, initDots]);

  return (
    <canvas
      ref={canvasRef}
      className={`absolute inset-0 w-full h-full ${className}`}
      style={{ pointerEvents: 'none' }}
    />
  );
}
