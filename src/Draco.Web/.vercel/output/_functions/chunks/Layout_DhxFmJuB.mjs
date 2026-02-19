import { c as createComponent, d as addAttribute, h as renderHead, i as renderSlot, a as renderTemplate, b as createAstro } from './astro/server_C1xCvP2A.mjs';
import 'piccolore';
import 'clsx';
/* empty css                          */

const $$Astro = createAstro();
const $$Layout = createComponent(($$result, $$props, $$slots) => {
  const Astro2 = $$result.createAstro($$Astro, $$props, $$slots);
  Astro2.self = $$Layout;
  const { title } = Astro2.props;
  return renderTemplate`<html lang="en"> <head><meta charset="UTF-8"><meta name="description" content="Draco - Autonomous Cloud Governance & Observability"><meta name="viewport" content="width=device-width"><link rel="icon" type="image/svg+xml" href="/draco-colored.svg"><meta name="generator"${addAttribute(Astro2.generator, "content")}><title>${title} | Draco Sentinel</title><link rel="preconnect" href="https://fonts.googleapis.com"><link rel="preconnect" href="https://fonts.gstatic.com" crossorigin><link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;600;800;900&display=swap" rel="stylesheet">${renderHead()}</head> <body> <nav> <a href="/" class="logo"> <img src="/draco-colored.svg" alt="Draco Logo" class="logo-img logo-light"> <img src="/draco-white.svg" alt="Draco Logo" class="logo-img logo-dark"> </a> <div class="nav-links"> <a href="#features">Features</a> <a href="/docs" style="margin-left: 20px;">Docs</a> </div> </nav> ${renderSlot($$result, $$slots["default"])} <footer> <p>&copy; 2026 Draco Governance Sentinel. Built for Secure Clouds.</p> <div class="legal-links"> <a href="/terms">Terms of Service</a> <a href="/privacy">Privacy Policy</a> </div> </footer> </body></html>`;
}, "/Users/taufeeqali/Projects/Draco/src/Draco.Web/src/layouts/Layout.astro", void 0);

export { $$Layout as $ };
