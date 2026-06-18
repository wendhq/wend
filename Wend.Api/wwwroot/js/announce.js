export function createAnnouncer(region) {
  return (message) => {
    region.textContent = "";
    requestAnimationFrame(() => {
      region.textContent = message;
    });
  };
}
