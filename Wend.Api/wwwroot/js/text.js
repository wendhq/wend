// Shared text normalisation for user-entered content.

/**
 * Upper-cases the first character and leaves the rest of the string exactly as typed, so a card
 * called "buy milk" is stored as "Buy milk".
 *
 * Applied to titles, names and checklist text only — never to an email address, never to a
 * password, and not to card notes, which are prose the writer is free to start however they like.
 *
 * The one guard: a word whose SECOND letter is a capital was deliberate — iOS, eBay, iPhone — and
 * "IOS crash" is worse than the lowercase it replaced. Everything else is left to the simple rule,
 * because guessing at proper nouns is a bigger promise than this is worth.
 */
export function capitaliseFirst(value) {
  const text = value ?? "";
  if (/^[a-z][A-Z]/.test(text)) return text;
  return text.charAt(0).toUpperCase() + text.slice(1);
}
