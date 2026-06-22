// Shared HTML-escaping for view modules — escapes the five characters that matter in our
// templates. Imported wherever a view interpolates user text into innerHTML.
export function escapeHtml(s) {
    return s.replace(/[&<>"']/g, (c) =>
        ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]
    );
}