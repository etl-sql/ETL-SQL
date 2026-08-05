### Security

- **An `Author` folder grant conferred `Manage` authority.** `FolderPermissionService.HasPermissionAsync`
  compared permissions with `>=`, which reads the enum's *storage* value. `Author` is stored as `3`
  and `Manage` as `2` — Author was appended rather than inserted so that adding it would not
  renumber every ACL row already in force — while its authority ranks *below* Manage. So
  `Author >= Manage` was true, and every folder-level check gated on `Manage` admitted an Author.

  Demonstrated before it was fixed: a Publisher-role user holding only `Author` on a folder could
  `POST /api/studio/reports` into it and receive `201 Created`. Publishing a new report into the
  folder is one of the acts the Author grant is explicitly defined not to permit. The same check
  gates dataset moves between folders and several folder routes.

  Fixed by using `AtLeast()`, which ranks rather than compares.

- **The guard that was supposed to prevent this could not see it.**
  `NoProductionCode_ComparesPermissionsOrdinallyAgainstAnythingAboveRead` matches a *literal*
  `FolderPermission.Execute|Manage|Author` on the line. The offending comparison was
  `effective.Value >= required` — two variables, no literal — so it read as clean. A second check
  now covers variable-to-variable comparisons in any file that deals in folder permissions, and it
  names the exact line when it fires.

  `DatasetPermission` was checked and is unaffected: its storage order is its authority order, so
  `>=` is correct there.

### Added

- `FolderPermissionEscalationTests` asserts both directions at the HTTP level — `Author` is refused
  and `Manage` succeeds — because denying everyone would have satisfied the negative case while
  removing the feature.
