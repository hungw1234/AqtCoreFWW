// k6 shared auth helpers — API Key scheme (X-Api-Key header)
// Usage: import { getApiKeyHeaders, getJsonApiKeyHeaders } from './lib/auth.js';
// Run with: k6 run <test>.js --env API_KEY=<key>

/**
 * Returns headers with X-Api-Key for API Key auth scheme.
 * Throws if API_KEY env var is not set.
 */
export function getApiKeyHeaders(extraHeaders = {}) {
  const apiKey = __ENV.API_KEY;
  if (!apiKey) {
    throw new Error('API_KEY env var required. Pass via: --env API_KEY=<key>');
  }
  return {
    'Accept': 'application/json',
    'X-Api-Key': apiKey,
    ...extraHeaders,
  };
}

/** JSON content-type variant for POST/PUT requests */
export function getJsonApiKeyHeaders() {
  return getApiKeyHeaders({ 'Content-Type': 'application/json' });
}
