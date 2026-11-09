# Changelog

All notable changes to this package are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## 0.2.0 — unreleased

Amounts in US dollars. API v2 now answers every amount in dollars beside the credits; the SDK
has a `decimal` property for each and steers you to it. Nothing is removed, so 0.1.0 code still
compiles (with CS0618 warnings where it reads a credit property).

- New properties, each US dollars to four decimals: `ChargedUsd` on `JobStarted` and
  `RenderStarted`; `ChargedUsd` and `RefundedUsd` on `Job`; `RefundedUsd` on `JobCanceled`;
  `BalanceUsd` on `Account`, `DailyLimitUsd` on `AccountKey` and `DailyUsd` on `AccountLimits`
  (null is no limit); `RenderUsd` and `EditUsd` on `EnginePrices`; `Usd` on `VideoPrice`;
  `BalanceUsd` and `ThresholdUsd` on the `credits.low` webhook's `CreditsLow`.
- `[Obsolete]` (removed in a later release): `CreditsCharged`, `CreditsRefunded`,
  `Account.Credits` (`AccountCredits.Spendable`), `DailyCreditLimit`, `DailyCredits`,
  `EnginePrices.Render` and `Edit`, `VideoPrice.Credits`, and `CreditsLow.Balance` and
  `Threshold`. Each message names the dollar property to read instead.
- `spec/openapi.json` refreshed (Nov 9 2026). A coverage test now also checks that the
  `[Obsolete]` properties are exactly the fields the spec deprecates.
- README reads the dollar properties.

## 0.1.0

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
