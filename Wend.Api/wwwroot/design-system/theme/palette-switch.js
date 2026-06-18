/*
 * Palette switch — click handler only.
 * Sets the active brand palette (data-palette) on <html> and remembers it.
 * The initial palette is restored by the inline <head> snippet to avoid a flash.
 * See theme-init-snippet.html. Markup: <button data-palette-set="gold">Gold</button>
 */
(function () {
	document.addEventListener("click", function (event) {
		const target = event.target.closest("[data-palette-set]");
		if (!target) return;

		const next = target.dataset.paletteSet;
		document.documentElement.dataset.palette = next;
		localStorage.setItem("palette", next);
	});
})();
