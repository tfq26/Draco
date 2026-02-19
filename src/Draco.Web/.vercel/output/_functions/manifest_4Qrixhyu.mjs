import 'piccolore';
import { j as decodeKey } from './chunks/astro/server_C1xCvP2A.mjs';
import 'clsx';
import { N as NOOP_MIDDLEWARE_FN } from './chunks/astro-designed-error-pages_Cx2PjMDF.mjs';
import 'es-module-lexer';

function sanitizeParams(params) {
  return Object.fromEntries(
    Object.entries(params).map(([key, value]) => {
      if (typeof value === "string") {
        return [key, value.normalize().replace(/#/g, "%23").replace(/\?/g, "%3F")];
      }
      return [key, value];
    })
  );
}
function getParameter(part, params) {
  if (part.spread) {
    return params[part.content.slice(3)] || "";
  }
  if (part.dynamic) {
    if (!params[part.content]) {
      throw new TypeError(`Missing parameter: ${part.content}`);
    }
    return params[part.content];
  }
  return part.content.normalize().replace(/\?/g, "%3F").replace(/#/g, "%23").replace(/%5B/g, "[").replace(/%5D/g, "]");
}
function getSegment(segment, params) {
  const segmentPath = segment.map((part) => getParameter(part, params)).join("");
  return segmentPath ? "/" + segmentPath : "";
}
function getRouteGenerator(segments, addTrailingSlash) {
  return (params) => {
    const sanitizedParams = sanitizeParams(params);
    let trailing = "";
    if (addTrailingSlash === "always" && segments.length) {
      trailing = "/";
    }
    const path = segments.map((segment) => getSegment(segment, sanitizedParams)).join("") + trailing;
    return path || "/";
  };
}

function deserializeRouteData(rawRouteData) {
  return {
    route: rawRouteData.route,
    type: rawRouteData.type,
    pattern: new RegExp(rawRouteData.pattern),
    params: rawRouteData.params,
    component: rawRouteData.component,
    generate: getRouteGenerator(rawRouteData.segments, rawRouteData._meta.trailingSlash),
    pathname: rawRouteData.pathname || void 0,
    segments: rawRouteData.segments,
    prerender: rawRouteData.prerender,
    redirect: rawRouteData.redirect,
    redirectRoute: rawRouteData.redirectRoute ? deserializeRouteData(rawRouteData.redirectRoute) : void 0,
    fallbackRoutes: rawRouteData.fallbackRoutes.map((fallback) => {
      return deserializeRouteData(fallback);
    }),
    isIndex: rawRouteData.isIndex,
    origin: rawRouteData.origin
  };
}

function deserializeManifest(serializedManifest) {
  const routes = [];
  for (const serializedRoute of serializedManifest.routes) {
    routes.push({
      ...serializedRoute,
      routeData: deserializeRouteData(serializedRoute.routeData)
    });
    const route = serializedRoute;
    route.routeData = deserializeRouteData(serializedRoute.routeData);
  }
  const assets = new Set(serializedManifest.assets);
  const componentMetadata = new Map(serializedManifest.componentMetadata);
  const inlinedScripts = new Map(serializedManifest.inlinedScripts);
  const clientDirectives = new Map(serializedManifest.clientDirectives);
  const serverIslandNameMap = new Map(serializedManifest.serverIslandNameMap);
  const key = decodeKey(serializedManifest.key);
  return {
    // in case user middleware exists, this no-op middleware will be reassigned (see plugin-ssr.ts)
    middleware() {
      return { onRequest: NOOP_MIDDLEWARE_FN };
    },
    ...serializedManifest,
    assets,
    componentMetadata,
    inlinedScripts,
    clientDirectives,
    routes,
    serverIslandNameMap,
    key
  };
}

const manifest = deserializeManifest({"hrefRoot":"file:///Users/taufeeqali/Projects/Draco/src/Draco.Web/","cacheDir":"file:///Users/taufeeqali/Projects/Draco/src/Draco.Web/node_modules/.astro/","outDir":"file:///Users/taufeeqali/Projects/Draco/src/Draco.Web/dist/","srcDir":"file:///Users/taufeeqali/Projects/Draco/src/Draco.Web/src/","publicDir":"file:///Users/taufeeqali/Projects/Draco/src/Draco.Web/public/","buildClientDir":"file:///Users/taufeeqali/Projects/Draco/src/Draco.Web/dist/client/","buildServerDir":"file:///Users/taufeeqali/Projects/Draco/src/Draco.Web/dist/server/","adapterName":"@astrojs/vercel","routes":[{"file":"","links":[],"scripts":[],"styles":[],"routeData":{"type":"page","component":"_server-islands.astro","params":["name"],"segments":[[{"content":"_server-islands","dynamic":false,"spread":false}],[{"content":"name","dynamic":true,"spread":false}]],"pattern":"^\\/_server-islands\\/([^/]+?)\\/?$","prerender":false,"isIndex":false,"fallbackRoutes":[],"route":"/_server-islands/[name]","origin":"internal","_meta":{"trailingSlash":"ignore"}}},{"file":"","links":[],"scripts":[],"styles":[],"routeData":{"type":"endpoint","isIndex":false,"route":"/_image","pattern":"^\\/_image\\/?$","segments":[[{"content":"_image","dynamic":false,"spread":false}]],"params":[],"component":"node_modules/astro/dist/assets/endpoint/generic.js","pathname":"/_image","prerender":false,"fallbackRoutes":[],"origin":"internal","_meta":{"trailingSlash":"ignore"}}},{"file":"","links":[],"scripts":[],"styles":[{"type":"inline","content":":root{--obsidian-black: #101010;--pure-spirit-white: #F8F9FA;--dragon-red: #B11116;--meteo-bronze: #8C5D3E;--destructor-blue: #1A369A;--titanium-silver: #BCC6CC;--gunmetal-grey: #4B5320;--bg-color: var(--obsidian-black);--text-color: var(--pure-spirit-white);--sub-text-color: #94a3b8;--accent-color: var(--dragon-red);--accent-glow: rgba(177, 17, 22, .4);--card-bg: rgba(255, 255, 255, .03);--border-color: rgba(188, 198, 204, .2);--font-main: \"Inter\", system-ui, -apple-system, sans-serif}@media(prefers-color-scheme:light){:root{--bg-color: var(--pure-spirit-white);--text-color: var(--obsidian-black);--sub-text-color: var(--destructor-blue);--card-bg: rgba(0, 0, 0, .05);--border-color: rgba(0, 0, 0, .1);--accent-glow: rgba(177, 17, 22, .2)}}*{box-sizing:border-box;margin:0;padding:0}body{background-color:var(--bg-color);color:var(--text-color);font-family:var(--font-main);line-height:1.6;overflow-x:hidden;background-image:radial-gradient(circle at 50% -20%,rgba(177,17,22,.12),transparent),radial-gradient(circle at 10% 50%,rgba(26,54,154,.08),transparent);min-height:100vh;transition:background-color .3s ease,color .3s ease}a{color:var(--accent-color);text-decoration:none;transition:all .2s ease}a:hover{text-shadow:0 0 8px var(--accent-glow)}.container{max-width:1000px;margin:0 auto;padding:2rem}nav{display:flex;justify-content:space-between;align-items:center;padding:1.5rem 2rem;border-bottom:1px solid var(--border-color);backdrop-filter:blur(10px);position:sticky;top:0;z-index:100}.logo{display:flex;align-items:center}.logo-img{height:40px;width:auto;transition:transform .3s ease}.logo:hover .logo-img{transform:scale(1.1)}.logo-light{display:none}.logo-dark{display:block}@media(prefers-color-scheme:light){.logo-light{display:block}.logo-dark{display:none}}.hero{padding:8rem 2rem;text-align:center}.hero h1{font-size:4rem;font-weight:900;margin-bottom:1.5rem;letter-spacing:-2px;line-height:1.1;color:var(--text-color)}.hero p{font-size:1.5rem;color:var(--sub-text-color);max-width:600px;margin:0 auto 2.5rem;font-weight:500}.btn{background:var(--dragon-red);color:#fff;padding:.8rem 2rem;border-radius:8px;font-weight:600;box-shadow:0 4px 14px 0 var(--accent-glow);border:1px solid var(--meteo-bronze)}.card{background:var(--card-bg);border:1px solid var(--border-color);border-radius:16px;padding:2rem;margin-top:4rem;backdrop-filter:blur(12px)}footer{margin-top:8rem;padding:4rem 2rem;border-top:1px solid var(--border-color);text-align:center;color:#64748b;font-size:.9rem}.legal-content{max-width:800px;margin:4rem auto}.legal-content h1{font-size:2.5rem;margin-bottom:2rem;border-bottom:2px solid var(--dragon-red);display:inline-block;padding-bottom:.5rem;color:var(--text-color)}.legal-content h2{margin:2rem 0 1rem;color:var(--meteo-bronze)}.legal-content p{margin-bottom:1rem;color:var(--text-color);opacity:.9}.legal-links{display:flex;gap:1.5rem;justify-content:center;margin-top:1rem}.feature-title{color:var(--destructor-blue)!important;margin-bottom:.5rem;text-transform:uppercase;letter-spacing:1px;font-weight:800}\n"}],"routeData":{"route":"/changelog/[version]","isIndex":false,"type":"page","pattern":"^\\/changelog\\/([^/]+?)\\/?$","segments":[[{"content":"changelog","dynamic":false,"spread":false}],[{"content":"version","dynamic":true,"spread":false}]],"params":["version"],"component":"src/pages/changelog/[version].astro","prerender":false,"fallbackRoutes":[],"distURL":[],"origin":"project","_meta":{"trailingSlash":"ignore"}}},{"file":"","links":[],"scripts":[],"styles":[{"type":"inline","content":":root{--obsidian-black: #101010;--pure-spirit-white: #F8F9FA;--dragon-red: #B11116;--meteo-bronze: #8C5D3E;--destructor-blue: #1A369A;--titanium-silver: #BCC6CC;--gunmetal-grey: #4B5320;--bg-color: var(--obsidian-black);--text-color: var(--pure-spirit-white);--sub-text-color: #94a3b8;--accent-color: var(--dragon-red);--accent-glow: rgba(177, 17, 22, .4);--card-bg: rgba(255, 255, 255, .03);--border-color: rgba(188, 198, 204, .2);--font-main: \"Inter\", system-ui, -apple-system, sans-serif}@media(prefers-color-scheme:light){:root{--bg-color: var(--pure-spirit-white);--text-color: var(--obsidian-black);--sub-text-color: var(--destructor-blue);--card-bg: rgba(0, 0, 0, .05);--border-color: rgba(0, 0, 0, .1);--accent-glow: rgba(177, 17, 22, .2)}}*{box-sizing:border-box;margin:0;padding:0}body{background-color:var(--bg-color);color:var(--text-color);font-family:var(--font-main);line-height:1.6;overflow-x:hidden;background-image:radial-gradient(circle at 50% -20%,rgba(177,17,22,.12),transparent),radial-gradient(circle at 10% 50%,rgba(26,54,154,.08),transparent);min-height:100vh;transition:background-color .3s ease,color .3s ease}a{color:var(--accent-color);text-decoration:none;transition:all .2s ease}a:hover{text-shadow:0 0 8px var(--accent-glow)}.container{max-width:1000px;margin:0 auto;padding:2rem}nav{display:flex;justify-content:space-between;align-items:center;padding:1.5rem 2rem;border-bottom:1px solid var(--border-color);backdrop-filter:blur(10px);position:sticky;top:0;z-index:100}.logo{display:flex;align-items:center}.logo-img{height:40px;width:auto;transition:transform .3s ease}.logo:hover .logo-img{transform:scale(1.1)}.logo-light{display:none}.logo-dark{display:block}@media(prefers-color-scheme:light){.logo-light{display:block}.logo-dark{display:none}}.hero{padding:8rem 2rem;text-align:center}.hero h1{font-size:4rem;font-weight:900;margin-bottom:1.5rem;letter-spacing:-2px;line-height:1.1;color:var(--text-color)}.hero p{font-size:1.5rem;color:var(--sub-text-color);max-width:600px;margin:0 auto 2.5rem;font-weight:500}.btn{background:var(--dragon-red);color:#fff;padding:.8rem 2rem;border-radius:8px;font-weight:600;box-shadow:0 4px 14px 0 var(--accent-glow);border:1px solid var(--meteo-bronze)}.card{background:var(--card-bg);border:1px solid var(--border-color);border-radius:16px;padding:2rem;margin-top:4rem;backdrop-filter:blur(12px)}footer{margin-top:8rem;padding:4rem 2rem;border-top:1px solid var(--border-color);text-align:center;color:#64748b;font-size:.9rem}.legal-content{max-width:800px;margin:4rem auto}.legal-content h1{font-size:2.5rem;margin-bottom:2rem;border-bottom:2px solid var(--dragon-red);display:inline-block;padding-bottom:.5rem;color:var(--text-color)}.legal-content h2{margin:2rem 0 1rem;color:var(--meteo-bronze)}.legal-content p{margin-bottom:1rem;color:var(--text-color);opacity:.9}.legal-links{display:flex;gap:1.5rem;justify-content:center;margin-top:1rem}.feature-title{color:var(--destructor-blue)!important;margin-bottom:.5rem;text-transform:uppercase;letter-spacing:1px;font-weight:800}\n"}],"routeData":{"route":"/docs","isIndex":true,"type":"page","pattern":"^\\/docs\\/?$","segments":[[{"content":"docs","dynamic":false,"spread":false}]],"params":[],"component":"src/pages/docs/index.astro","pathname":"/docs","prerender":false,"fallbackRoutes":[],"distURL":[],"origin":"project","_meta":{"trailingSlash":"ignore"}}},{"file":"","links":[],"scripts":[],"styles":[{"type":"inline","content":":root{--obsidian-black: #101010;--pure-spirit-white: #F8F9FA;--dragon-red: #B11116;--meteo-bronze: #8C5D3E;--destructor-blue: #1A369A;--titanium-silver: #BCC6CC;--gunmetal-grey: #4B5320;--bg-color: var(--obsidian-black);--text-color: var(--pure-spirit-white);--sub-text-color: #94a3b8;--accent-color: var(--dragon-red);--accent-glow: rgba(177, 17, 22, .4);--card-bg: rgba(255, 255, 255, .03);--border-color: rgba(188, 198, 204, .2);--font-main: \"Inter\", system-ui, -apple-system, sans-serif}@media(prefers-color-scheme:light){:root{--bg-color: var(--pure-spirit-white);--text-color: var(--obsidian-black);--sub-text-color: var(--destructor-blue);--card-bg: rgba(0, 0, 0, .05);--border-color: rgba(0, 0, 0, .1);--accent-glow: rgba(177, 17, 22, .2)}}*{box-sizing:border-box;margin:0;padding:0}body{background-color:var(--bg-color);color:var(--text-color);font-family:var(--font-main);line-height:1.6;overflow-x:hidden;background-image:radial-gradient(circle at 50% -20%,rgba(177,17,22,.12),transparent),radial-gradient(circle at 10% 50%,rgba(26,54,154,.08),transparent);min-height:100vh;transition:background-color .3s ease,color .3s ease}a{color:var(--accent-color);text-decoration:none;transition:all .2s ease}a:hover{text-shadow:0 0 8px var(--accent-glow)}.container{max-width:1000px;margin:0 auto;padding:2rem}nav{display:flex;justify-content:space-between;align-items:center;padding:1.5rem 2rem;border-bottom:1px solid var(--border-color);backdrop-filter:blur(10px);position:sticky;top:0;z-index:100}.logo{display:flex;align-items:center}.logo-img{height:40px;width:auto;transition:transform .3s ease}.logo:hover .logo-img{transform:scale(1.1)}.logo-light{display:none}.logo-dark{display:block}@media(prefers-color-scheme:light){.logo-light{display:block}.logo-dark{display:none}}.hero{padding:8rem 2rem;text-align:center}.hero h1{font-size:4rem;font-weight:900;margin-bottom:1.5rem;letter-spacing:-2px;line-height:1.1;color:var(--text-color)}.hero p{font-size:1.5rem;color:var(--sub-text-color);max-width:600px;margin:0 auto 2.5rem;font-weight:500}.btn{background:var(--dragon-red);color:#fff;padding:.8rem 2rem;border-radius:8px;font-weight:600;box-shadow:0 4px 14px 0 var(--accent-glow);border:1px solid var(--meteo-bronze)}.card{background:var(--card-bg);border:1px solid var(--border-color);border-radius:16px;padding:2rem;margin-top:4rem;backdrop-filter:blur(12px)}footer{margin-top:8rem;padding:4rem 2rem;border-top:1px solid var(--border-color);text-align:center;color:#64748b;font-size:.9rem}.legal-content{max-width:800px;margin:4rem auto}.legal-content h1{font-size:2.5rem;margin-bottom:2rem;border-bottom:2px solid var(--dragon-red);display:inline-block;padding-bottom:.5rem;color:var(--text-color)}.legal-content h2{margin:2rem 0 1rem;color:var(--meteo-bronze)}.legal-content p{margin-bottom:1rem;color:var(--text-color);opacity:.9}.legal-links{display:flex;gap:1.5rem;justify-content:center;margin-top:1rem}.feature-title{color:var(--destructor-blue)!important;margin-bottom:.5rem;text-transform:uppercase;letter-spacing:1px;font-weight:800}\n"}],"routeData":{"route":"/","isIndex":true,"type":"page","pattern":"^\\/$","segments":[],"params":[],"component":"src/pages/index.astro","pathname":"/","prerender":false,"fallbackRoutes":[],"distURL":[],"origin":"project","_meta":{"trailingSlash":"ignore"}}}],"base":"/","trailingSlash":"ignore","compressHTML":true,"componentMetadata":[["\u0000astro:content",{"propagation":"in-tree","containsHead":false}],["/Users/taufeeqali/Projects/Draco/src/Draco.Web/src/pages/[...slug].astro",{"propagation":"in-tree","containsHead":true}],["\u0000@astro-page:src/pages/[...slug]@_@astro",{"propagation":"in-tree","containsHead":false}],["\u0000@astrojs-ssr-virtual-entry",{"propagation":"in-tree","containsHead":false}],["/Users/taufeeqali/Projects/Draco/src/Draco.Web/src/pages/docs/index.astro",{"propagation":"in-tree","containsHead":true}],["\u0000@astro-page:src/pages/docs/index@_@astro",{"propagation":"in-tree","containsHead":false}],["/Users/taufeeqali/Projects/Draco/src/Draco.Web/src/pages/changelog/[version].astro",{"propagation":"none","containsHead":true}],["/Users/taufeeqali/Projects/Draco/src/Draco.Web/src/pages/index.astro",{"propagation":"none","containsHead":true}]],"renderers":[],"clientDirectives":[["idle","(()=>{var l=(n,t)=>{let i=async()=>{await(await n())()},e=typeof t.value==\"object\"?t.value:void 0,s={timeout:e==null?void 0:e.timeout};\"requestIdleCallback\"in window?window.requestIdleCallback(i,s):setTimeout(i,s.timeout||200)};(self.Astro||(self.Astro={})).idle=l;window.dispatchEvent(new Event(\"astro:idle\"));})();"],["load","(()=>{var e=async t=>{await(await t())()};(self.Astro||(self.Astro={})).load=e;window.dispatchEvent(new Event(\"astro:load\"));})();"],["media","(()=>{var n=(a,t)=>{let i=async()=>{await(await a())()};if(t.value){let e=matchMedia(t.value);e.matches?i():e.addEventListener(\"change\",i,{once:!0})}};(self.Astro||(self.Astro={})).media=n;window.dispatchEvent(new Event(\"astro:media\"));})();"],["only","(()=>{var e=async t=>{await(await t())()};(self.Astro||(self.Astro={})).only=e;window.dispatchEvent(new Event(\"astro:only\"));})();"],["visible","(()=>{var a=(s,i,o)=>{let r=async()=>{await(await s())()},t=typeof i.value==\"object\"?i.value:void 0,c={rootMargin:t==null?void 0:t.rootMargin},n=new IntersectionObserver(e=>{for(let l of e)if(l.isIntersecting){n.disconnect(),r();break}},c);for(let e of o.children)n.observe(e)};(self.Astro||(self.Astro={})).visible=a;window.dispatchEvent(new Event(\"astro:visible\"));})();"]],"entryModules":{"\u0000noop-middleware":"_noop-middleware.mjs","\u0000virtual:astro:actions/noop-entrypoint":"noop-entrypoint.mjs","\u0000@astro-page:node_modules/astro/dist/assets/endpoint/generic@_@js":"pages/_image.astro.mjs","\u0000@astro-page:src/pages/changelog/[version]@_@astro":"pages/changelog/_version_.astro.mjs","\u0000@astro-page:src/pages/docs/index@_@astro":"pages/docs.astro.mjs","\u0000@astro-page:src/pages/index@_@astro":"pages/index.astro.mjs","\u0000@astro-page:src/pages/[...slug]@_@astro":"pages/_---slug_.astro.mjs","\u0000@astrojs-ssr-virtual-entry":"entry.mjs","\u0000@astro-renderers":"renderers.mjs","\u0000@astrojs-ssr-adapter":"_@astrojs-ssr-adapter.mjs","\u0000@astrojs-manifest":"manifest_4Qrixhyu.mjs","/Users/taufeeqali/Projects/Draco/src/Draco.Web/node_modules/astro/dist/assets/services/sharp.js":"chunks/sharp_Dd_sE-ul.mjs","/Users/taufeeqali/Projects/Draco/src/Draco.Web/.astro/content-assets.mjs":"chunks/content-assets_DleWbedO.mjs","/Users/taufeeqali/Projects/Draco/src/Draco.Web/.astro/content-modules.mjs":"chunks/content-modules_Dz-S_Wwv.mjs","\u0000astro:data-layer-content":"chunks/_astro_data-layer-content_5pKuVj4n.mjs","astro:scripts/before-hydration.js":""},"inlinedScripts":[],"assets":["/draco-black.svg","/draco-colored.svg","/draco-white.svg","/favicon.ico","/favicon.svg","/changelogs/v1.0.0.json"],"buildFormat":"directory","checkOrigin":true,"allowedDomains":[],"serverIslandNameMap":[],"key":"Zsl6LfulLjXQDZBQ9brCjeXiWJKlcbAByFkNWLMkiFY="});
if (manifest.sessionConfig) manifest.sessionConfig.driverModule = null;

export { manifest };
