# Users, roles and permissions

Phase 9. Spec: `specs/phase-9-multiuser.md` §2, §4a D2/D4. Checkpoints C9.2 and C9.5.

## The feature

Who may open the books, and what each of them may do. Application accounts rather than Windows
logins, roles as editable data rather than a fixed list, and every permission checked in the service
layer so that bypassing the screen does not bypass the rule.

## Why application accounts and not Windows

The owner chose this at approval (§4a D2), and the deciding reason is the audit trail rather than
convenience. Non-negotiable #3 requires every create, alter and delete to name a user. A small shop
on a workgroup typically has one Windows login per machine, shared by whoever is sitting at it —
under Windows authentication every action would be stamped with that machine's habitual user. That
is technically a name and practically useless, and worse than useless in a dispute, because it looks
authoritative.

## The user workflow

### First launch after the upgrade

The books open under a name, so the sign-in window appears before the shell. On an install with no
accounts it offers to create the **owner account** instead of asking for credentials nobody has.

**Nothing is seeded.** No user name, no default password. A shipped `admin`/`admin` that somebody
has to remember to change is how products end up with one nobody changed.

Until that first account exists, **every permission is granted to everybody**. That is deliberate:
enforcement has to begin somewhere, and refusing everything before the first account would mean the
first account could never be created. It is also exactly how Phase 8 behaved, so an owner who has
not got round to this loses nothing — and gains nothing either, which is the point of doing it.

### Signing in

User name and password. A wrong password and an unknown user name report the same message and take
the same time to do it — on a two or three person install the set of valid names is small enough
that confirming one would hand a stranger half the answer.

An account whose password was set by the owner is routed to **change it** rather than refused: an
operator who cannot get in also cannot change their password. This is what makes the owner knowing
the initial password temporary, and it is what makes the audit trail's attribution to that account
mean anything afterwards.

### Administration → Users

| Key | Does |
|---|---|
| `Alt+C` | New account |
| `Ctrl+A` | Save |
| `Alt+D` | Deactivate the selected account |
| `Alt+X` | Reload the selected account into the editor |

Pick a role with the lookup field, type an initial password, save. The account is flagged to change
that password at first sign-in and there is no way to turn the flag off.

**Accounts are deactivated, never deleted.** The audit trail refers to them by name for as long as
the books exist, so a dangling author would be worse than a disabled row. There is no delete button.

### Administration → Roles

Owner and Operator are seeded at startup. Tick what a role may do and save.

Two things the screen refuses, both for the same reason:

- **The owner role's grants cannot be edited.** It holds every permission by definition, including
  ones a later release adds. Snapshotting today's list would mean an owner silently losing access to
  a new feature, and the first person to notice would be the owner.
- **The last role able to manage users cannot lose that right**, and nor can the last account
  holding it be deactivated or demoted. Otherwise there is a one-click route to a state whose only
  remedy is editing the database by hand, and on a shop-floor install there is nobody to do that.

## What each role starts with

Read literally off §4a D4 — the Operator "bills and takes receipts, and is refused price override,
voucher cancellation and configuration".

| Permission | Owner | Operator |
|---|---|---|
| Manage users | ✓ | |
| Manage configuration | ✓ | |
| Manage masters | ✓ | ✓ |
| Post voucher | ✓ | ✓ |
| Alter voucher | ✓ | |
| Cancel voucher | ✓ | |
| Override sales price | ✓ | |
| View reports | ✓ | ✓ |
| Run statutory returns | ✓ | |
| Run payroll | ✓ | |
| Import / export data | ✓ | |

Masters are included for the Operator because one who cannot create a customer cannot bill a new
one. Returns, payroll and data migration are excluded as owner work rather than billing.

**This is a starting point, not a ruling.** Roles are data (D4), so the owner changes any of it on
the Roles screen without a release. Worth confirming at walkthrough.

## Developer notes

- `PermissionKind` is an **enum**, and that is not in tension with roles being data. Each value is a
  branch some service actually takes, so the set is closed by the code whether it is written down or
  not; a grant naming something nothing checks would read as protection and provide none. The type
  carries `Kind` because CA1711 reserves the `Permission` suffix and this build treats warnings as
  errors — the reason is recorded on the type so nobody renames it back.
- `IPermissionAuthority` **re-reads the user row and the role on every check**, rather than
  snapshotting at sign-in. Two indexed single-row reads at the two or three counters D1 sizes is
  noise, and it buys two properties worth more than the saving: revoking a permission or
  deactivating an account bites at once rather than at the holder's next sign-in, which on a shop
  floor could be days; and a role reassignment cannot be defeated by staying signed in.
- **Where the gates are:** `VoucherPostingService.PostAsync` / `AlterAsync` / `CancelAsync`, every
  `IUserAdminService` member, and the sales price override. The override needs the company-wide
  Sales setting **and** the permission — requiring both is what makes the upgrade
  behaviour-identical rather than quietly widening who may override.
- Passwords are stored only as a PBKDF2-HMAC-SHA256 verifier at 600,000 iterations with a
  per-password salt, and the stored value carries its own scheme, cost and salt so the cost can be
  raised later without a schema change or locking existing accounts out. `ICredentialProtector` from
  Phase 6 is deliberately **not** used: that protects secrets this application must replay to the
  IRP and so has to be reversible. A password must never be recoverable, including by us.
- `SessionCurrentUser` makes `ICurrentUser` read through the session, falling back to the Windows
  identity with a `(no sign-in)` prefix. The fallback is not a convenience — provisioning runs
  before any login prompt could be shown, and `ICurrentUser` must never resolve to blank.
- Password policy is a stated default, not a spec requirement: a minimum of eight characters and
  nothing else. Deliberately not a composition rule — those push people towards a predictable shape
  and towards writing the result on a note beside the counter, which on a shop floor is the actual
  threat model.

## Verification

- `PermissionEnforcementTests` — 20 tests through the real composition against a real database, with
  no window in the process. The headline is C9.5: an Operator calling
  `IVoucherPostingService.CancelAsync` straight off the container is refused, and the voucher id is
  deliberately one that does not exist, so a `PermissionDeniedException` rather than a "no such
  voucher" proves the gate runs *before* the service reads anything.
- `Pbkdf2PasswordHasherTests` — round trip, per-password salt, the verifier carrying its own
  parameters, nothing resembling the password in the stored value, and eight malformed stored values
  that must fail the sign-in rather than throw.
- `Phase9ScreenTests` — the three login states, the refusal shown on the Users screen rather than
  swallowed, the owner role's grants locked, and a role's grants round-tripping.
- `EveryWindowInTheApplication_LoadsWithTheApplicationResources` — added here because no module
  lists a startup window, so nothing was constructing one. It caught two defects in this phase's own
  work.
