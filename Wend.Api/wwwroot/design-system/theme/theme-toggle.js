/*
 * Theme toggle — click handler only.
 * The initial theme is set by the inline <head> snippet to avoid flash.
 * See design-system/theme/theme-init-snippet.html for the snippet to copy into <head>.
 */
(function () {
	document.addEventListener("click", function (event) {
		const target = event.target.closest("[data-theme-toggle]");
		if (!target) return;

		const root = document.documentElement;
		const next = root.dataset.theme === "light" ? "dark" : "light";
		root.dataset.theme = next;
		localStorage.setItem("theme", next);
	});
})();
