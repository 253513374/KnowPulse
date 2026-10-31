export function keys(prefix) {
  return Object.keys(sessionStorage).filter((key) => key.startsWith(prefix));
}
