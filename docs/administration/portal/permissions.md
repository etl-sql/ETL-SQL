# Groups and Folder Permissions
<!-- GrantPortalPermissionStatement -->
<!-- RevokePortalPermissionStatement -->
<!-- GrantPortalDatasetPermissionStatement -->
<!-- RevokePortalDatasetPermissionStatement -->

## By deployment profile

| Profile | What applies |
| :--- | :--- |
| **Solo / Workstation** | **N/A.** One trusted operator, no Portal, and nobody to grant anything to. |
| **Team / SME** | The whole of this page. This is the smallest profile where permissions mean anything, and where the two-axis model starts to matter: a **role** decides the class of operation, an **ACL** decides which resources. |
| **Enterprise / Corporate** | As Team, with groups sourced from the directory, so removing someone from a group revokes their reports, saved views and share links. Grants made to a user *personally* deliberately survive losing a group. |
| **SaaS / Departmental** | As Enterprise, **defined separately per environment**. A resource id from one environment is meaningless in another and a token minted in one is refused by the other, so grants never span environments — but you must create them in each. |

Two facts worth knowing before you design a permission scheme:

- **ACLs bind to groups, not roles.** `DataSteward` is a role; granting a folder to "data stewards"
  means creating a *group* and adding them to it.
- **Admins bypass folder ACLs entirely.** Granting the Admin group access to a folder is a no-op.

## Groups & Folder Permissions

Folder visibility is controlled through **groups** and **ACLs** (access control lists).

### Groups

A group is a named collection of users. Open **Admin → Groups** to create groups and add members.

Use the group search box to locate groups by name, description, or directory mapping. The member panel is also server-paged: search active users when adding members, select multiple matches to add them together, or select current members to remove them together. **Delete Selected** rejects groups that still have members or ACL entries; remove those references first or use the administrative API with an explicit cascade decision.

### Folder ACLs

Each folder can have one or more ACL entries, each granting a group a permission level:

| Permission | What it allows |
| :--- | :--- |
| `Read` | See the folder and its reports; view snapshots |
| `Execute` | Run reports and build new snapshots |
| `Author` | Everything `Execute` allows, plus editing a report's script, content, and metadata |
| `Manage` | Everything `Author` allows, plus publishing new reports, moving them, deleting them, and sharing them |

`Author` exists for the person who maintains a report without administering the folder it lives in.
An Author may rewrite a report entirely; they may **not** move it to another folder, delete it,
create share links or embed tokens, or publish a new report into the folder. Moving a report changes
what two folders contain and deleting one changes what a folder contains — neither is an act on the
report's content, which is the only thing an Author was given authority over.

> [!IMPORTANT]
> Holding `Manage` on a folder is authority over the **reports in it**, not over the folder itself.
> Reading or changing a folder's ACL, creating a subfolder, and deleting a folder are reserved to the
> `Admin` role. Without that split the strongest grant would be self-propagating: whoever held it
> could hand it out, so the set of people with access could only ever grow.

ACLs are not inherited — a group must be explicitly granted access to each folder it needs to see. A folder with no ACLs is visible only to Admins **and its owner**: the user who created a folder (or received it through ownership transfer) always holds effective `Manage` on it, without an ACL entry. Ownership moves only through the explicit transfer on user deletion (see [User Management](users.md)); revoking a group ACL never locks an owner out of their own folder.

### Protected Branches and Review

When the Portal writes scripts back to git (`Portal:SourceControl`), name the branches that must not
receive an unreviewed change:

```jsonc
"Portal": {
  "SourceControl": { "ProtectedBranches": [ "main", "release/*" ] },
  "Studio": { "RequireApprovalToPublish": true }
}
```

A pattern is an exact branch name, or a prefix when it ends in `*`. Both settings are empty/off by
default, and they are meant to be turned on together: protecting a branch without a review path only
blocks people, and providing a review path without protecting anything only asks nicely.

With both on, a change reaching a protected branch has been read by someone other than its author.
The reviewer's name is written into a `Reviewed-by:` commit trailer with the script hash, so the
review is visible in `git log` without the Portal's database. A refused commit is audited as
`COMMIT_REPORT_SCRIPT_DENIED`.

> [!NOTE]
> Approval is checked against the **published script hash**, not the most recent draft. A draft that
> was approved but never published cannot lend its approval to whatever is on disk now.

---

> [!TIP]
> Create an **Everyone** group, add all users to it, and grant it `Read` on public folders rather than individually managing each user.

---

