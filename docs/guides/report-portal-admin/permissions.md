# Groups and Folder Permissions

## 5. Groups & Folder Permissions

Folder visibility is controlled through **groups** and **ACLs** (access control lists).

### 5.1 Groups

A group is a named collection of users. Open **Admin → Groups** to create groups and add members.

Use the group search box to locate groups by name, description, or directory mapping. The member panel is also server-paged: search active users when adding members, select multiple matches to add them together, or select current members to remove them together. **Delete Selected** rejects groups that still have members or ACL entries; remove those references first or use the administrative API with an explicit cascade decision.

### 5.2 Folder ACLs

Each folder can have one or more ACL entries, each granting a group a permission level:

| Permission | What it allows |
| :--- | :--- |
| `Read` | See the folder and its reports; view snapshots |
| `Execute` | Run reports and build new snapshots |
| `Manage` | Publish, update, and delete reports within the folder |

ACLs are not inherited — a group must be explicitly granted access to each folder it needs to see. A folder with no ACLs is visible only to Admins **and its owner**: the user who created a folder (or received it through ownership transfer) always holds effective `Manage` on it, without an ACL entry. Ownership moves only through the explicit transfer on user deletion (§4.7); revoking a group ACL never locks an owner out of their own folder.

> [!TIP]
> Create an **Everyone** group, add all users to it, and grant it `Read` on public folders rather than individually managing each user.

---

