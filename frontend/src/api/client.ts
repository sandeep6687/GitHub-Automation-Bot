// In production, the Vercel rewrite proxies /api/* to the Render backend
// server-side, so the browser always talks to the same origin. This means:
//   - No cross-site cookie problem
//   - SameSite=Lax is sufficient for the session cookie
//   - credentials:'include' still needed so cookies ride along with every fetch
//
// In local dev, the Vite proxy in vite.config.ts forwards /api/* to
// http://localhost:5000, so the same relative URL works.
//
// VITE_API_URL is only set when you want to override the API base explicitly
// (e.g. point a local frontend at a staging backend). Leave it empty in both
// dev and production normal deployments.
const API_BASE = import.meta.env.VITE_API_URL || '';

export async function apiFetch<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
  const url = `${API_BASE}${endpoint}`;
  const headers = new Headers(options.headers || {});

  if (options.body && typeof options.body === 'string' && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  const response = await fetch(url, {
    ...options,
    headers,
    credentials: 'include', // Required: send the HttpOnly session cookie with every request
  });

  if (!response.ok) {
    let errorMessage = `HTTP error ${response.status}: ${response.statusText}`;
    try {
      const errorJson = await response.json();
      if (errorJson && errorJson.error) {
        errorMessage = errorJson.error;
      }
    } catch {
      // Use status text if response is not JSON
    }
    throw new Error(errorMessage);
  }

  if (response.status === 204) {
    return {} as T;
  }

  return response.json();
}

// The OAuth login endpoint navigates the browser to start the OAuth flow.
// Using a same-origin relative URL ensures the oauth_state cookie is set
// on the same origin as the callback, eliminating the state-cookie
// cross-origin mismatch that caused the auth loop.
export function getAuthLoginUrl(): string {
  return `${API_BASE}/api/auth/login`;
}
