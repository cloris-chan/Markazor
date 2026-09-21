self.importScripts('./service-worker-assets.js');
self.importScripts('./_markazor/routes.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));
self.addEventListener('message', event => {
    if (event.data?.type === 'SKIP_WAITING') {
        self.skipWaiting();
    }
});

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff2?$/, /\.png$/, /\.svg$/, /\.webp$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/, /\.md$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/, /(^|\/)_markazor\/content\/drafts\//, /(^|\/)staticwebapp\.config\.json$/ ];

const baseUrl = new URL('/', self.origin);
const manifestUrlList = new Set(self.assetsManifest.assets.map(asset => toAssetUrl(asset.url).href));

function toAssetUrl(path) {
    const encodedPath = path
        .split('/')
        .map(segment => encodeURIComponent(segment))
        .join('/');
    return new URL(encodedPath, baseUrl);
}

async function onInstall(event) {
    console.info('Service worker: Install');

    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(toAssetUrl(asset.url).href, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
    await self.clients.claim();
}

async function onFetch(event) {
    const request = event.request;
    const url = new URL(request.url);
    if (request.method !== 'GET' || url.origin !== self.origin || url.pathname.startsWith('/api/')) {
        return fetch(request);
    }
    const cache = await caches.open(cacheName);
    if (request.mode === 'navigate') {
        const path = url.pathname.length > 1 ? url.pathname.replace(/\/$/, '') : '/';
        const page = self.markazorRoutes[path];
        const admin = /^\/(?:studio|setup|auth)(?:\/|$)/.test(path);
        const target = page || (admin ? '/_markazor/app.html' : null);
        if (target) {
            const response = await cache.match(target);
            if (response) return response;
            return fetch(new URL(target, baseUrl));
        }
    } else if (manifestUrlList.has(url.href)) {
        const response = await cache.match(request);
        if (response) return response;
    }
    try {
        return await fetch(request);
    } catch {
        if (request.mode === 'navigate') {
            const missing = await cache.match('/404.html');
            if (missing) return new Response(await missing.text(), {status: 404, headers: {'Content-Type': 'text/html; charset=utf-8'}});
        }
        return new Response('This resource is not available offline.', {status: 503, headers: {'Content-Type': 'text/plain; charset=utf-8'}});
    }
}
