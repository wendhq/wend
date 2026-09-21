// The icon set.
//
// Plain inline SVG, one stroke weight, one 24-unit grid. stroke: currentColor so
// every icon inherits the colour of the control it sits in and follows the theme
// with it. No dependency, no sprite, no network request: the markup is a template
// string, which also keeps `img-src` out of a consumer's CSP.
//
// Why not emoji: every platform draws them differently, they carry their own
// colour into a themed UI, they sit on the text baseline instead of on the
// button's centre, and a screen reader may read the character's name out loud
// in the middle of a label.
//
// Every icon is decorative. The control around it carries the name — an
// aria-label on an icon-only button, visible text beside it otherwise — so each
// one is aria-hidden and focusable="false" (legacy-Edge SVGs are focusable by
// default, which would otherwise add a phantom tab stop).
//
// Usage (any module consumer, e.g. a view):
//   import { icon } from "../design-system/components/icons.js";
//   `<button class="btn" type="button">${icon("plus")} Add</button>`
//   `<button class="btn icon-btn" type="button" aria-label="Close">${icon("close")}</button>`
//   `<div class="empty-state-icon">${icon("search", { size: 48 })}</div>`

const PATHS = {
	// A record: outer edge, run-out groove, label.
	disc: '<circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="5"/><circle cx="12" cy="12" r="1.2" fill="currentColor" stroke="none"/>',
	plus: '<path d="M12 5.5v13M5.5 12h13"/>',
	close: '<path d="M6.5 6.5l11 11M17.5 6.5l-11 11"/>',
	check: '<path d="M4.5 12.5l5 5 10-11"/>',
	search: '<circle cx="10.5" cy="10.5" r="6.5"/><path d="M15.5 15.5L21 21"/>',
	pin: '<path d="M12 21.5s7-6.6 7-11.5a7 7 0 1 0-14 0c0 4.9 7 11.5 7 11.5z"/><circle cx="12" cy="10" r="2.5"/>',
	calendar:
		'<rect x="3.5" y="5" width="17" height="15.5" rx="2.5"/><path d="M8 3v4M16 3v4M3.5 10h17"/>',
	star: '<path d="M12 3.5l2.7 5.6 6.1.85-4.45 4.3 1.06 6.05L12 17.4l-5.41 2.9 1.06-6.05L3.2 9.95l6.1-.85z"/>',
	lock: '<rect x="4.5" y="10" width="15" height="10.5" rx="2.5"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/>',
	image: '<rect x="3" y="4.5" width="18" height="15" rx="2.5"/><circle cx="8.75" cy="9.75" r="1.6"/><path d="M20.5 15.5l-4.75-4.75L7 19.5"/>',
	moon: '<path d="M20 14.2A8.4 8.4 0 0 1 9.8 4 7.4 7.4 0 1 0 20 14.2z"/>',
	sun: '<circle cx="12" cy="12" r="4.2"/><path d="M12 2.5v2.2M12 19.3v2.2M4.28 4.28l1.56 1.56M18.16 18.16l1.56 1.56M2.5 12h2.2M19.3 12h2.2M4.28 19.72l1.56-1.56M18.16 5.84l1.56-1.56"/>',
	menu: '<path d="M4 7h16M4 12h16M4 17h16"/>',
};

// The names the set ships, for a gallery or a test to walk.
export const ICON_NAMES = Object.freeze(Object.keys(PATHS));

// size is the drawn box in CSS pixels; strokeWidth thins out as it grows, so a
// 48px empty-state icon does not read as a heavier version of a 20px one.
// An unknown name returns "" — a typo renders nothing rather than throwing
// inside a view's template.
export function icon(name, { size = 20, strokeWidth } = {}) {
	const path = PATHS[name];
	if (!path) return "";

	const width = strokeWidth ?? (size >= 40 ? 1.25 : 1.75);

	return /*HTML*/ `<svg class="icon" width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="${width}" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">${path}</svg>`;
}
