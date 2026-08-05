### Changed

- **The administration docs are readable by deployment profile.** They were a mechanical split of
  three large manuals and still read like one: 28 files carrying orphaned section numbers that
  started at `4.1` or `## 6.` with no sections 1–3 anywhere, and content that mixed profiles
  silently — a Solo reader working through "Secrets and Keys" met the Portal JWT secret and the
  Orchestrator API key, neither of which exists on a workstation.

  - **All orphaned numbering removed** across 28 files, along with the duplicate `# Title` /
    `## Title` pairs the numbers were hiding, and the resulting heading-level skips.
  - **New `docs/administration/by-profile.md`** gives Solo,
    Team, Enterprise and SaaS each an ordered path through the same task-oriented pages. The docs
    stay organised by task — a fact still lives in exactly one place — and this is the other axis.
  - **A `## By deployment profile` band on the eleven pages where behaviour genuinely differs**,
    saying plainly what each profile does and which are **N/A**. Reference-only pages did not get
    one; a band that says "same for all profiles" trains readers to skip the band.

- **Fourteen dangling `§` cross-references now point somewhere.** The split left references like
  "see §9 below" and "(§11.3)" aimed at sections of the old monolithic manual that no longer exist
  under those numbers — dead navigation that no link checker catches, because they were never
  links. Each is now a real link or has been reworded.

- **Four genuinely broken anchors fixed**, including a link into a heading that had been deleted as
  a duplicate.

- **Generated section indexes no longer describe pages by their first heading.** Fifteen pages had
  no prose between the title and the first section, so the generator quoted things like
  "## 8. Backup & Maintenance" as the description. Each now opens with a sentence saying what the
  page is for, which improves the page and the index together.
