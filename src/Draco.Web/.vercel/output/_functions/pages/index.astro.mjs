import { c as createComponent, r as renderComponent, a as renderTemplate, m as maybeRenderHead } from '../chunks/astro/server_C1xCvP2A.mjs';
import 'piccolore';
import { $ as $$Layout } from '../chunks/Layout_DhxFmJuB.mjs';
export { renderers } from '../renderers.mjs';

const $$Index = createComponent(($$result, $$props, $$slots) => {
  return renderTemplate`${renderComponent($$result, "Layout", $$Layout, { "title": "Autonomous Cloud Governance" }, { "default": ($$result2) => renderTemplate` ${maybeRenderHead()}<main> <section class="hero"> <h1>Cloud Governance on <span style="color: var(--dragon-red);">Autopilot</span></h1> <p>The open-source sentinel that watches your cloud and fixes issues before they hit your apps.</p> <div class="hero-actions"> <a href="https://github.com/tfq26/Draco" class="btn">View on GitHub</a> </div> </section> <div class="container"> <section id="features" class="card"> <h2 style="margin-bottom: 2rem; font-size: 2rem; color: var(--text-color);">Why Draco?</h2> <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(250px, 1fr)); gap: 2rem;"> <div> <h3 class="feature-title">AI-Powered</h3> <p>Leverages Google Gemini to analyze infrastructure metadata and provide human-readable remediation.</p> </div> <div> <h3 class="feature-title">GitOps Driven</h3> <p>Automatically opens Pull Requests to fix misconfigurations. You review, you approve, it deploys.</p> </div> <div> <h3 class="feature-title">Privacy First</h3> <p>Self-hosted architecture. We never see your data. Your clouds, your keys, your control.</p> </div> </div> </section> </div> </main> ` })}`;
}, "/Users/taufeeqali/Projects/Draco/src/Draco.Web/src/pages/index.astro", void 0);

const $$file = "/Users/taufeeqali/Projects/Draco/src/Draco.Web/src/pages/index.astro";
const $$url = "";

const _page = /*#__PURE__*/Object.freeze(/*#__PURE__*/Object.defineProperty({
    __proto__: null,
    default: $$Index,
    file: $$file,
    url: $$url
}, Symbol.toStringTag, { value: 'Module' }));

const page = () => _page;

export { page };
