import { FadeIn } from '@/components/animations';
import { CodeBlock } from '@/components/code-block';
import { Download, Package, Rocket } from 'lucide-react';

const steps = [
  {
    step: '1',
    title: 'Install the CLI',
    description: 'Install the UCP command line tool via npm, cargo, or download a prebuilt binary.',
    code: `npm install -g @mflrevan/ucp`,
    filename: 'terminal',
    icon: Download,
  },
  {
    step: '2',
    title: 'Install the Bridge',
    description: 'Add the Unity Editor bridge package to your project. This sets up the WebSocket server.',
    code: `cd /path/to/MyUnityProject
ucp install`,
    filename: 'terminal',
    icon: Package,
  },
  {
    step: '3',
    title: 'Connect & Automate',
    description: 'With Unity open, connect and start controlling the editor programmatically.',
    code: `ucp connect                    # verify connection
ucp snapshot                   # capture scene hierarchy
ucp write-file Assets/S.cs    # write project files
ucp play                      # enter play mode
ucp screenshot -o capture.png # grab screenshots
ucp vcs commit -m "update"    # commit via VCS`,
    filename: 'terminal',
    icon: Rocket,
  },
];

export function QuickStart() {
  return (
    <section className="py-24 relative" id="quickstart">
      {/* Subtle background accent */}
      <div className="absolute inset-0 -z-10 overflow-hidden">
        <div className="absolute top-1/2 left-0 w-100 h-100 bg-primary/4 rounded-full blur-[150px] -translate-y-1/2" />
        <div className="absolute bottom-0 right-0 w-75 h-75 bg-primary/3 rounded-full blur-[120px]" />
      </div>

      <div className="mx-auto max-w-4xl px-6">
        <FadeIn>
          <div className="text-center mb-16">
            <p className="text-primary font-medium text-sm tracking-wider uppercase mb-3">Quick Start</p>
            <h2 className="text-3xl sm:text-4xl font-bold tracking-tight">
              Up and running in{' '}
              <span className="bg-linear-to-r from-primary to-purple-400 bg-clip-text text-transparent">3 steps</span>
            </h2>
            <p className="mt-4 text-lg text-muted-foreground">From install to full Unity control in under a minute.</p>
          </div>
        </FadeIn>

        <div className="relative space-y-10">
          {/* Timeline connector */}

          {steps.map((step, i) => (
            <FadeIn key={step.step} delay={0.15 * i}>
              <div className="grid md:grid-cols-[200px_1fr] gap-6 items-start relative">
                <div className="flex md:flex-col items-start md:items-start gap-4 md:gap-3">
                  <div className="relative z-10 inline-flex items-center justify-center w-10 h-10 rounded-xl bg-linear-to-br from-primary/20 to-primary/5 border border-primary/25 text-primary font-bold text-sm shrink-0">
                    <step.icon className="h-5 w-5" />
                  </div>
                  <div>
                    <h3 className="font-semibold text-lg">{step.title}</h3>
                    <p className="text-sm text-muted-foreground mt-1 leading-relaxed">{step.description}</p>
                  </div>
                </div>
                <CodeBlock code={step.code} title={step.filename} />
              </div>
            </FadeIn>
          ))}
        </div>
      </div>
    </section>
  );
}
