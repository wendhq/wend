export async function api(path, options = {}) {
  const res = await fetch(path, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  if (!res.ok) {
    // Callers need the code to tell "not signed in" apart from a genuine failure.
    const error = new Error(`${res.status} ${res.statusText}`);
    error.status = res.status;
    // Some failures carry a machine-readable reason. The reset screen has to tell "your password
    // is too short" apart from "your link is dead", and both are 400. Every other caller reads only
    // .status; an empty or non-JSON body leaves this null instead of throwing.
    error.body = await res.json().catch(() => null);
    throw error;
  }
  return res.status === 204 ? null : res.json();
}
