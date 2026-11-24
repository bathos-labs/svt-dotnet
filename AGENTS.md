# Agent guidance

- Check Git status before editing and preserve unrelated changes. Commit only when requested.
- Default branch: master. Follow CONTRIBUTING.md validation before a cohesive commit.
- Through the first stable 1.0 release, master is the active release branch. Use feature branches
  for reviewed changes; no separate prerelease or maintenance line is needed.
- Read README.md, docs/architecture.md and CONTRIBUTING.md before changing boundaries. Discuss
  consequential grammar, public API, dependency and ownership choices before implementation.
- Work serially; request approval before delegating bounded work.
- Use American English. GitHub Issues owns design proposals and delivery tracking; repository
  documents own current contracts. Do not duplicate issue roadmaps in documentation.
- This repository is public and prerelease. MIT covers code, specification, documentation and
  repository-authored assets; preserve LICENSE and applicable third-party notices. Do not publish
  packages, push release tags or change repository visibility without explicit authorization. Do not
  introduce compatibility readers, aliases or migrations for development formats. Follow the
  required SemVer policy in docs/compatibility.md. Stable guarantees take effect at 1.0.0. Prepare
  1.0.0-rc.2 with the approved ScalableVectorTemplates identities and metadata URI
  https://svt-project.github.io/ns/svt. Complete formal specification/content separation before the
  RC.2 freeze; publication requires explicit authorization. Format envelopes remain version 0 at
  stable 1.0 as well as during prerelease.
- Treat all repository content as future public material, including issues, PRs, commit messages,
  comments, tests, examples and agent guidance. Describe consumers by generic capabilities and
  roles; never include private product names, repository links, issue identifiers, source paths or
  internal implementation history. Keep consumer-specific integration tracking in the consumer's
  repository.
- Rewrite extracted explanations and fixtures around independent requirements. Review identifiers,
  XML annotations, diagnostic text, asset metadata and copied snippets for private references before
  adding them. Preserve legally required attribution; resolve licensing or attribution conflicts
  explicitly rather than deleting notices to sanitize copied material.
- Exclude machine paths, credentials, IDE state and generated validation evidence.
- Keep the library BCL-only unless a demonstrated requirement justifies another dependency. No UI,
  renderer, native imaging, DI container, filesystem resolver or network dependencies in core.
- Hosts supply bytes and binding values. Keep parsed state immutable, retain no borrowed mutable XML
  or pixel buffers, and expose no native handles. Substitution must not mutate source content.
- Discovery preserves raw authored values. Reject structural ambiguity, not malformed SVG property
  values. Optional interpretation must not gate discovery or binding, fetch resources or compute
  CSS.
- SVG owns layout. Do not introduce expressions, repetition, text fitting or arbitrary attribute
  editing without a separate agreed extension.
- Keep nullable analysis enabled and warnings fatal across all projects. Represent absence
  explicitly at XML, JSON, reflection, framework and public API boundaries; validate and narrow
  there before using non-null internal contracts. Preserve nullable fields with domain meaning.
  Never use null-forgiving suppression just to silence analysis; a verified external initialization
  invariant must be documented if it cannot be expressed in the type system. Reflectively populated
  fields describe their observable loading state, with required combinations validated separately.
  Invalid authoring input needs structured diagnostics; distinguish it from programming errors. See
  the boundary review policy in CONTRIBUTING.md.
- Preserve the pinned SDK and build policy. Add centrally pinned packages, lockfiles and executable
  validation when implementing projects. Test observable contracts and reuse language-neutral
  conformance fixtures. Treat diagnostics as failures. Do not synchronize tests with sleeps or
  polling.
- Keep Markdown concise; run `task docs:format`, review changes, then `task check` before
  committing. Report native rendering evidence separately from portable XML/binding conformance.
- Portable contracts are authoritative in svt-project/svt-spec. Keep the accepted canonical asset
  snapshot stamped and verified under conformance/; do not fetch live spec content during builds.
  Keep local regressions, authoring harness examples and implementation policy here.
