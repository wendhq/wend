export function createAnnouncer(region) {
  return (message) => {
    region.textContent = "";
    // A short timeout (not rAF, which pauses in hidden/throttled tabs) lets the clear register
    // so identical consecutive messages still re-announce.
    setTimeout(() => {
      region.textContent = message;
    }, 120);
  };
}
