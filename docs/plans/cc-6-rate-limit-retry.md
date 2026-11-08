# CC-6: Uploading a big folder doesn't fail on the rate limit

The server allows 200 API requests a minute per user (fixed window) and answers the rest with
429 and `Retry-After: <seconds>`. `connapse upload` sends one request per file and counted a 429 as a
failed file.

1. **Retry policy** — `ApiClient` waits and retries a 429 (or a 503 with `Retry-After`), honoring
   `Retry-After` plus jitter, else exponential backoff with jitter, at most 5 retries.
   Done when every command sent through `ApiClient` retries; `connapse api` still shows the raw 429.
2. **Tests** — unit tests for the wait rules, and a 311-file upload against a fake server enforcing
   200 requests a minute. Done when they pass.
3. **Ship** — Release build and tests with CI's flags, PR into `main` naming the sources.
   Done when the PR is open and the PM is told.
- Phase 1: RateLimitRetry.cs added, ApiClient.cs sends through it; passthrough unchanged.
- Phase 2: RateLimitRetryTests.cs, UploadCommandTests.cs, CommandHost.cs; 226 pass in Release. Next: live 311-file upload against the pinned server, then PR.
- Phase 3: live run against the pinned server (default 200/min): 311/311 uploaded, exit 0, one 62s wait; server stats show 311 documents. CI-flag Release build + tests green. Next: PR.
