# Changelog

All notable changes to this package are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## 0.1.0 — unreleased

First release.

- Every endpoint of API v2: `Renders`, `Edits`, `Enhancements`, `Videos`, `Jobs` (get, cancel,
  wait, wait-all, stream), `Files.DownloadAsync`, `Account`, `Engines`.
- `CreateAndWaitAsync` on every create: a render waits in the held answer (`wait=90`) and reads
  on only for what is still running.
- Retries for a 429, a 5xx or a dropped connection, after `Retry-After` (up to 60 s) or a
  backoff, with one idempotency key per call kept across them.
- Images as files, bytes, links, data URIs or base64 (`ImageInput`).
- `Webhooks.Verify` and `Webhooks.Sign` (Standard Webhooks, HMAC-SHA256).
- `netstandard2.0` and `net8.0`. Request and answer types are checked against the API's OpenAPI
  document by the tests.
