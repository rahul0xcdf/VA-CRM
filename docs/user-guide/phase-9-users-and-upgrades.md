# Users, sign-in and database upgrades

What changes on screen in Phase 9, for the person using the software.

## The first time you open it after this update

### 1. The database needs upgrading

A window appears listing the changes waiting to be applied.

**Do this from one machine, with every other counter closed.** Two computers upgrading the same
database at the same time can damage it, which is why the software no longer does it on its own.

Press **Apply upgrade now**. When it finishes, the books open.

If something goes wrong the message stays on screen and the books stay shut. That is deliberate —
better than opening accounts the software cannot read properly. Write down what it says.

### 2. Create the owner account

The sign-in window appears and offers to create the first account, because there isn't one yet.

Enter a user name (what you will type to sign in), your name (what appears in the books against
everything you do), and a password twice. Eight characters or more.

**There is no default account and no default password.** Nothing to change, nothing to forget.

> **Until you create this account, everyone who opens the software can do everything.** That is how
> the software behaved before this update, so nothing has got worse — but nothing has got better
> either until you do this.

## Every time you open it afterwards

Type your user name and password. The books open under your name, and every voucher, master and
correction is recorded against it.

If you get it wrong, the software says only that the name and password do not match. It will not
tell you which — and it would not tell a stranger either.

### If someone else set your password

You will be asked to choose your own before you can carry on. You cannot skip it.

This matters: whoever created your account typed that first password, so they know it. Until you
change it, "you" in the books could have been them.

## Giving somebody else access

**Administration → Users**

1. `Alt+C` for a new account.
2. Type their user name and name, pick a **role**, type a starting password.
3. `Ctrl+A` to save.

Tell them the starting password. They will be made to change it the first time they sign in, so you
will not know their working password — which is the point.

| Key | Does |
|---|---|
| `Alt+C` | New account |
| `Ctrl+A` | Save |
| `Alt+D` | Stop an account signing in |
| `Alt+X` | Put the selected account back in the form |

### Somebody has left

**Deactivate** them — don't look for a delete button, there isn't one. Their name still appears
against everything they did, for as long as you keep the books, and that record is worth more than a
tidy list.

### Resetting a forgotten password

Select the account, type a new password in the form, press **Reset password**. They will be made to
change it when they next sign in.

## What each role may do

**Administration → Roles**

The software starts with two:

- **Owner** — everything.
- **Operator** — bills, takes receipts, creates and edits masters, reads reports. Cannot change a
  price away from the price list, cannot cancel a posted voucher, cannot change settings, cannot run
  returns or payroll, cannot import or export.

Tick or untick what a role may do and save. You can add roles of your own — an Accountant who sees
the books and the returns but not pricing, say.

Two things the software will not let you do:

- **Change what the Owner role may do.** It is meant to be able to do everything, including things
  added in a future update.
- **Take away the last person's ability to manage users.** If it allowed that, nobody would be able
  to give it back.

## Worth knowing

**Two people editing the same thing.** If you and somebody else open the same customer and you both
save, the second person is told their change was not applied and asked to reload. Nothing is lost
silently. Before this update, the second save would have quietly erased the first.

**Invoice numbers.** Two counters billing at the same moment will never be given the same number,
and no number is skipped. If the software is busy it may ask you to try again rather than hand out a
number it is not sure of.

## If you get stuck

The status line at the foot of every screen says what happened. If a screen says you are not allowed
to do something, that is the role you are signed in under — ask whoever holds the Owner account.
