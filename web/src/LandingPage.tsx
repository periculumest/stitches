import { useState } from 'react';
import { ArrowDown, ArrowRight, BookOpen, Check, CheckCheck, Flower2, MousePointer2, RotateCcw, ShieldCheck, Cable as Spool, Upload } from 'lucide-react';
import './landing.css';

const palette = ['#385b46', '#72917c', '#bdcda0', '#b9655d', '#e6ada3', '#c49a48'];

// A small, public illustration of the garden sampler; no account data is loaded.
const garden = (() => {
  const stitches: { x: number; y: number; color: number }[] = [];
  for (let y = 0; y < 64; y++) for (let x = 0; x < 64; x++) {
    const px = (x + .5) * 100 / 64 - 50, py = (y + .5) * 100 / 64 - 50;
    const radius = Math.hypot(px, py);
    let color = radius > 33 && radius < 35 ? 0 : -1;
    for (let leaf = 0; leaf < 20; leaf++) {
      const angle = leaf * Math.PI * 2 / 20;
      const lx = px - Math.cos(angle) * 35, ly = py - Math.sin(angle) * 35;
      const u = lx * Math.cos(angle + .8) + ly * Math.sin(angle + .8);
      const v = -lx * Math.sin(angle + .8) + ly * Math.cos(angle + .8);
      if (u * u / 46 + v * v / 9 < 1) color = v > 0 ? 1 : 2;
    }
    for (const [cx, cy, scale] of [[-18, -23, 1], [22, -17, .8], [-23, 19, .85], [15, 28, 1.1], [0, -35, .65]]) {
      const fx = (px - cx) / scale, fy = (py - cy) / scale, distance = Math.hypot(fx, fy);
      if (distance < 7.5 + Math.cos(Math.atan2(fy, fx) * 5) * 2) color = distance > 5 ? 4 : 3;
      if (distance < 2.5) color = 5;
    }
    if ((Math.abs(px) < 2 && Math.abs(py) < 11) || (Math.abs(py) < 2 && Math.abs(px) < 11)
      || (Math.abs(Math.abs(px) - Math.abs(py)) < 1.4 && Math.abs(px) < 7)) color = 5;
    if (color >= 0) stitches.push({ x, y, color });
  }
  return stitches;
})();
const goldStitches = garden.filter(stitch => stitch.color === 5).length;

function GardenPreview() {
  const [marked, setMarked] = useState(false);
  const complete = marked ? goldStitches : 0;
  const percent = Math.round(complete / garden.length * 100);
  return <div className="landing-preview-wrap">
    <div className="landing-orbit" aria-hidden="true"/>
    <div className="landing-preview">
      <div className="landing-preview-heading"><span className="landing-preview-icon"><Flower2 size={21}/></span><div><strong>The little garden</strong><span>A little, every day.</span></div><span className="landing-preview-label">INTERACTIVE PREVIEW</span></div>
      <div className="landing-preview-toolbar"><span><MousePointer2 size={13}/> At the hoop</span><span>64 × 64 <span aria-hidden="true">·</span> 6 colors</span></div>
      <svg className="landing-garden" viewBox="0 0 384 384" role="img" aria-label="A cross-stitch wreath with pink flowers, green leaves, and a golden star. Completed gold stitches fade when you try the preview.">
        <defs><pattern id="landing-grid" width="6" height="6" patternUnits="userSpaceOnUse"><path d="M 6 0 H 0 V 6" fill="none" stroke="#dedbc9" strokeWidth=".4"/></pattern></defs>
        <rect width="384" height="384" fill="#fcf9ef"/>
        <rect width="384" height="384" fill="url(#landing-grid)"/>
        {palette.map((color, index) => <path key={color} className="landing-stitches" fill="none" stroke={color} strokeWidth="1.65" strokeLinecap="round" opacity={marked && index === 5 ? .18 : 1}
          d={garden.filter(stitch => stitch.color === index).map(({ x, y }) => `M${x * 6 + 1.4} ${y * 6 + 1.4}l3.2 3.2m0 -3.2l-3.2 3.2`).join(' ')}/>)}
      </svg>
      <div className="landing-preview-bottom">
        <div className="landing-preview-progress"><span aria-live="polite">{complete} of {garden.length} stitches <strong>{percent}%</strong></span><div role="progressbar" aria-label="Preview stitches completed" aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent}><span style={{ width: `${percent}%` }}/></div></div>
        <button className="primary" onClick={() => setMarked(!marked)}>{marked ? <RotateCcw size={15}/> : <CheckCheck size={16}/>} {marked ? 'Undo preview stitches' : 'Try marking stitches'}</button>
      </div>
    </div>
    <div className="landing-thread-card" aria-hidden="true"><span className="landing-thread-caption">A PALETTE OF POSSIBILITIES</span><div>{palette.map(color => <span className="landing-bobbin" key={color}><i style={{ backgroundColor: color }}/></span>)}</div><span>Gather your colors. Find your calm.</span></div>
    <div className="landing-preview-note"><span><Check size={17}/></span><p>{marked ? 'That’s progress.' : 'Every stitch counts.'}<small>{marked ? 'Gold stitches marked. Try undo, too.' : 'Make a little, then pick up later.'}</small></p></div>
    <p className="landing-preview-caption">A tiny taste of your stitching space. Go on, give it a try.</p>
  </div>;
}

export function LandingPage({ error }: { error: string }) {
  return <div className="landing" id="top">
    <a className="landing-skip" href="#landing-main">Skip to content</a>
    <header className="landing-header">
      <a className="brand" href="#top" aria-label="Stitch Helper home"><span className="brand-mark"><Spool size={25}/></span><span>stitch<span className="brand-light">helper</span><small>A LITTLE, EVERY DAY.</small></span></a>
      <nav aria-label="Main navigation"><a className="landing-nav-link" href="#how-it-works">How it works</a><a className="landing-nav-link" href="#made-for-you">Made for your rhythm</a><a className="secondary landing-login" href="/auth/google">Log in <ArrowRight size={15}/></a></nav>
    </header>
    <main id="landing-main" tabIndex={-1}>
      <section className="landing-hero" aria-labelledby="landing-title">
        <div className="landing-hero-copy">
          <div className="landing-kicker"><span/> A QUIETER CORNER OF YOUR DAY</div>
          <h1 id="landing-title">A little less counting.<br/><em>A little more<br className="landing-title-break"/> stitching.</em></h1>
          <p className="landing-intro">Your patterns, your threads, your place in the chart. All together in one thoughtful space, so you can settle into the part you love.</p>
          {error && <div className="landing-error" role="alert">{error}</div>}
          <div className="landing-hero-actions"><a className="primary" href="/auth/google">Continue with Google <ArrowRight size={17}/></a><a className="landing-tour-link" href="#how-it-works">Take a look around <ArrowDown size={15}/></a></div>
          <p className="landing-private"><ShieldCheck size={15}/> Your own little space. Your patterns stay private.</p>
          <div className="landing-hero-footnote"><span aria-hidden="true">✳</span><p>For the one-more-row evenings.<br/>And the just-five-minutes mornings.</p></div>
        </div>
        <GardenPreview/>
      </section>
      <div className="landing-benefits" aria-label="Workspace features"><span><BookOpen size={19}/> A home for your patterns</span><span><CheckCheck size={20}/> Progress you can come back to</span><span><Spool size={20}/> Your threads, together</span></div>
      <section className="landing-how" id="how-it-works" aria-labelledby="landing-how-title">
        <div className="landing-section-heading"><div><div className="landing-kicker">FROM THE FIRST CROSS TO THE LAST</div><h2 id="landing-how-title">Less keeping track.<br/><em>More getting lost in it.</em></h2></div><p>A place for the practical things,<br/>so there’s more room for the lovely things.</p></div>
        <ol className="landing-steps">
          <li><div className="landing-step-top"><Upload size={24} strokeWidth={1.4}/><span>01</span></div><h3>Bring your next project.</h3><p>Import a supported PDF and review its chart, symbols, and colors. Or get a feel for things with the little garden sampler.</p><span className="landing-step-note">Your next beginning, all in one place.</span></li>
          <li><div className="landing-step-top"><CheckCheck size={26} strokeWidth={1.4}/><span>02</span></div><h3>Find your stitching rhythm.</h3><p>Focus on a color, zoom in on the details, and mark stitches as you go. Undo is always close by when plans change.</p><span className="landing-step-note">One stitch. One row. A little progress.</span></li>
          <li><div className="landing-step-top"><Spool size={26} strokeWidth={1.4}/><span>03</span></div><h3>Pick up right here.</h3><p>Keep your thread collection and saved progress with your account. Log in on another device and find your place again.</p><span className="landing-step-note">Ready whenever you are.</span></li>
        </ol>
      </section>
      <section className="landing-invitation" id="made-for-you" aria-labelledby="landing-invitation-title">
        <div className="landing-invitation-flower" aria-hidden="true"><Flower2 strokeWidth={.6}/></div>
        <div className="landing-kicker">NO RUSH. JUST ONE MORE STITCH.</div>
        <h2 id="landing-invitation-title">Something lovely.<br/><em>A little at a time.</em></h2>
        <p>For the projects in your basket and the ideas waiting their turn.<br/>Make yourself a little space to bring them to life.</p>
        <a className="primary" href="/auth/google">Log in to your stitching space <ArrowRight size={17}/></a>
        <span className="landing-invitation-note">Sign in securely with your Google account.</span>
      </section>
    </main>
    <footer className="landing-footer"><a className="landing-footer-brand" href="#top"><Spool size={19}/> Stitch Helper</a><p>A little time for something you love.</p><a href="/legal/terms-of-service">Terms of Service</a><a href="/legal/privacy-policy">Privacy Policy</a><a href="/legal">All legal documents</a><a href="/data-retention">Data retention &amp; deletion</a><a href="/auth/google">Log in <ArrowRight size={14}/></a></footer>
  </div>;
}
