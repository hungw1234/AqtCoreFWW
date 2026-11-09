import '@testing-library/jest-dom';

// jsdom does not implement scrollIntoView — polyfill for component tests
Element.prototype.scrollIntoView = () => {};

// jsdom does not implement ResizeObserver — polyfill for responsive components
globalThis.ResizeObserver = class ResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
} as unknown as typeof globalThis.ResizeObserver;

// Ant Design requires window.matchMedia — jsdom does not provide it
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }),
});

// ── Network guard ─────────────────────────────────────────────────────────
// Unit tests must NEVER hit a real network. In jsdom, an un-mocked axios/fetch
// call opens a real socket → ECONNREFUSED → an ASYNC AggregateError that, under
// the parallel fork pool, kills the worker (exit 127 / suite freeze) instead of
// failing one named test (row FE-TEST-SUITE-XHR-WORKER-CRASH).
//
// Fix: convert any un-mocked request into a SYNCHRONOUS throw. axios's XHR
// adapter runs `send()` inside its Promise executor, so a sync throw becomes a
// clean promise rejection (react-query enters error state) — no real socket, no
// async crash. A console.warn breadcrumb (incl. the current test name) surfaces
// the offending test even when the rejection is swallowed downstream.
//
// The guard is INERT for correctly-mocked tests: setting
// `apiClient.defaults.adapter` bypasses XHR entirely, and `vi.mock`-ing the api
// module makes no axios call at all — neither path reaches this code.
//
// Contract: assumes XHR is only ever driven via axios's promise-executor adapter.
// A future test issuing RAW XHR with an `onerror` handler would receive a sync
// throw from send() instead of an async error event (breaks rather than degrades).
// No such consumer exists today; revisit if one appears.
function networkGuardMessage(method: string, url: string): string {
  const currentTest =
    (globalThis as { expect?: { getState?: () => { currentTestName?: string } } }).expect
      ?.getState?.()
      .currentTestName ?? '<unknown test>';
  return (
    `[test-network-guard] Blocked un-mocked ${method} ${url} — a unit test triggered a ` +
    `REAL network request. Mock the api module (vi.mock) or apiClient.defaults.adapter. ` +
    `(test: ${currentTest})`
  );
}

const XHR = globalThis.XMLHttpRequest;
if (XHR && !(XHR.prototype as { __networkGuarded?: boolean }).__networkGuarded) {
  const originalOpen = XHR.prototype.open;
  type GuardedXhr = XMLHttpRequest & { __guardMethod?: string; __guardUrl?: string };

  // Keep the real open() so the object reaches OPENED state (setRequestHeader etc.
  // stay valid); record method+url for the send()-time diagnostic.
  XHR.prototype.open = function (this: GuardedXhr, method: string, url: string | URL, ...rest: unknown[]) {
    this.__guardMethod = method;
    this.__guardUrl = String(url);
    return (originalOpen as (...a: unknown[]) => void).apply(this, [method, url, ...rest]);
  } as typeof XHR.prototype.open;

  // Throw BEFORE any bytes leave — never opens a real socket.
  XHR.prototype.send = function (this: GuardedXhr) {
    const msg = networkGuardMessage(this.__guardMethod ?? 'GET', this.__guardUrl ?? '<unknown>');
    console.warn(msg);
    throw new Error(msg);
  } as typeof XHR.prototype.send;

  (XHR.prototype as { __networkGuarded?: boolean }).__networkGuarded = true;
}

if (typeof globalThis.fetch === 'function' && !(globalThis.fetch as { __networkGuarded?: boolean }).__networkGuarded) {
  const guardedFetch = ((input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    const msg = networkGuardMessage('FETCH', url);
    console.warn(msg);
    return Promise.reject(new Error(msg));
  }) as typeof globalThis.fetch;
  (guardedFetch as { __networkGuarded?: boolean }).__networkGuarded = true;
  globalThis.fetch = guardedFetch;
}

// Suppress Ant Design warnings in test output
const originalError = console.error.bind(console);
beforeAll(() => {
  console.error = (...args: unknown[]) => {
    const msg = String(args[0] ?? '');
    // Suppress known Ant Design / React act() noise
    if (msg.includes('Warning:') && msg.includes('antd')) return;
    if (msg.includes('inside a test was not wrapped in act')) return;
    originalError(...args);
  };
});

afterAll(() => {
  console.error = originalError;
});
