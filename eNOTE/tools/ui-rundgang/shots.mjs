// eNOTE Screenshot-Rundgang: node shots.mjs <ausgabeordner> [viewport...]
// viewports: tablet (1280x800), desktop (1440x900), mobile (390x844)
import { chromium } from 'playwright-core';
import fs from 'node:fs';

const BASE = 'https://localhost:62148';
const out = process.argv[2] ?? 'out';
const wanted = process.argv.slice(3);
const VIEWPORTS = { tablet: { width: 1280, height: 800 }, desktop: { width: 1440, height: 900 }, mobile: { width: 390, height: 844 } };
fs.mkdirSync(out, { recursive: true });

const browser = await chromium.launch({ channel: 'msedge', headless: true });
const settle = (page, ms = 900) => page.waitForTimeout(ms);

for (const [name, vp] of Object.entries(VIEWPORTS)) {
  if (wanted.length && !wanted.includes(name)) continue;
  const ctx = await browser.newContext({ viewport: vp, ignoreHTTPSErrors: true, deviceScaleFactor: 1, isMobile: name === 'mobile', hasTouch: name !== 'desktop' });
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(String(e)));
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  // Navigation wie ein Mensch: sichtbarer Link, am Handy über das „Plus“-Blatt (kein Reload → Zone bleibt offen)
  const gehe = async (route) => {
    const link = page.locator(`a[href="${route}"]:visible`).first();
    if (await link.count()) { await link.click(); return; }
    const plus = page.locator('.enote-nav-mobile button:visible');
    if (await plus.count()) {
      await plus.click(); await settle(page, 350);
      const imBlatt = page.locator(`.enote-sheet a[href="${route}"]`).first();
      if (await imBlatt.count()) { await imBlatt.click(); return; }
    }
    await page.goto(`${BASE}${route}`);
  };
  const shot = async (label) => {
    await settle(page);
    await page.screenshot({ path: `${out}/${name}-${label}.png` });
    // Prüfung: nichts darf breiter als der Bildschirm sein (sonst Querscrollen/Herauszoomen am Handy)
    const zuBreit = await page.evaluate((breite) => [...document.querySelectorAll('body *')]
      .filter(e => { const r = e.getBoundingClientRect(); return r.width > 0 && r.right > breite + 1 && getComputedStyle(e).position !== 'fixed' && !e.closest('.enote-nav-mobile, .kx-kats, .kx-unterkats, .pr-kats, .enote-tabs, .enote-table-scroll, .kx-ticket'); })
      .slice(0, 5).map(e => `${e.tagName}.${typeof e.className === 'string' ? e.className : ''}`), vp.width);
    if (zuBreit.length) errors.push(`[${label}] zu breit: ${zuBreit.join(', ')}`);
  };

  await page.goto(`${BASE}/`); await shot('01-landing');
  await page.goto(`${BASE}/auth/login`); await shot('02-login');
  await page.fill('input[name=pseudonym]', 'demo.proprio');
  await page.fill('input[name=password], input[type=password]', 'demo2026');
  await Promise.all([page.waitForNavigation(), page.click('#login-submit-btn')]);
  await settle(page, 1500); await shot('03-home');

  // Besitzer-Zone entsperren (Desktop-Sidebar oder mobile Bottom-Nav)
  let lock = page.locator('.enote-lock-card:visible').first();
  if (!(await lock.count()) && name === 'mobile') {
    await page.locator('.enote-nav-mobile button').click(); await settle(page, 400);
    lock = page.locator('.enote-sheet .enote-lock-card').first();
  }
  if (await lock.count()) {
    await lock.click(); await settle(page, 500);
    await page.locator('.enote-modal .enote-pin-feld').fill('1234');
    await page.keyboard.press('Enter'); await settle(page, 1200);
  }
  await shot('04-home-entsperrt');

  for (const [route, label] of [['/finances', '10-finances'], ['/services', '11-prestations'], ['/remises', '12-remises'],
    ['/ausgaben', '13-depenses'], ['/equipe', '14-equipe'], ['/abonnement', '15-abonnement'], ['/parametres', '16-parametres'], ['/profil', '17-profil']]) {
    await gehe(route);
    await settle(page, 1500); await shot(label);
    if (route === '/finances') { await page.screenshot({ path: `${out}/${name}-${label}-full.png`, fullPage: true }); }
  }

  // Kasse: Start-Screen, dann Sitzung öffnen (oder offene Sitzung übernehmen)
  await gehe('/caisse');
  await settle(page, 1500);
  const wieder = page.locator('.enote-modal-footer button', { hasText: 'Continuer' });
  if (await wieder.count()) { await shot('20-caisse-wiederhergestellt'); await wieder.click(); await settle(page, 400); }
  const kassierer = page.locator('.kx-kassierer-btn', { hasText: 'Awa' });
  if (await kassierer.count()) {
    await kassierer.click(); await settle(page, 300);
    await page.locator('.kx-start .enote-pin-feld').fill('2222');
    await shot('20-caisse-start');
    await page.locator('.kx-start .enote-btn-primary').click();
    await settle(page, 1800);
  }
  await shot('21-caisse-aktiv');
  for (const n of ['Brushing', 'Coupe homme', 'Coupe homme', 'Huile de karité 250 ml']) {
    const t = page.locator('.kx-kachel-haupt', { hasText: n }).first();
    if (await t.count()) { await t.click(); await settle(page, 250); }
  }
  await shot('22-caisse-ticket');
  const griff = page.locator('.kx-ticket-griff:visible');
  if (await griff.count()) { await griff.click(); await settle(page, 400); await shot('22b-caisse-ticket-offen'); }

  const oeffne = async (sel) => { const b = page.locator(sel); if (await b.count()) { await b.first().click(); await settle(page, 500); return true; } return false; };
  if (await oeffne('.kx-zahl-momo')) {
    await page.locator('.enote-anbieter-kachel').first().click(); await settle(page, 300);
    await shot('23-caisse-momo');
    await page.locator('.kx-pay-modus', { hasText: 'Mixte' }).click(); await settle(page, 300);
    await page.locator('.enote-betrag-feld').fill('5000'); await page.keyboard.press('Tab'); await settle(page, 400);
    await shot('24-caisse-mixte');
    await page.locator('.kx-pay-modus', { hasText: 'Espèces' }).click(); await settle(page, 300);
    await page.locator('.enote-betrag-schnell .enote-chip').last().click(); await settle(page, 300);
    await shot('25-caisse-bar');
    await page.locator('.enote-modal-footer .enote-btn-primary').click(); await settle(page, 1500);
    await shot('26-caisse-fertig');
  }
  if (await oeffne('.kx-aktion-haupt')) {
    await page.locator('.kx-abschluss .enote-betrag-feld').fill('25000'); await page.keyboard.press('Tab'); await settle(page, 400);
    await shot('27-caisse-abschluss');
    await page.keyboard.press('Escape');
  }

  fs.writeFileSync(`${out}/${name}-errors.txt`, errors.join('\n'));
  await ctx.close();
}
await browser.close();
console.log('fertig:', out);
