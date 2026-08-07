export async function api(path, options = {}) {
  const res = await fetch(path, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  if (!res.ok) {
    // Callers need the code to tell "not signed in" apart from a genuine failure.
    const error = new Error(`${res.status} ${res.statusText}`);
    error.status = res.status;
    throw error;
  }
  return res.status === 204 ? null : res.json();
}
