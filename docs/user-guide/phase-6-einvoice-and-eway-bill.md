# Phase 6 — E-invoicing & e-way bills

For the person using the software. Three screens, under **E-invoicing** on the Home menu.

> **Before you start:** the NIC sandbox credentials have not been supplied yet, so nothing on these
> screens can reach the portal. Everything described here is built and tested, but the steps that need
> a live portal — getting a real IRN, printing a real QR, issuing a real e-way bill — will wait until
> the credentials arrive. See `specs/open-questions.md` #10.

## Setting up (E-invoicing → E-invoice settings)

Do this once.

1. **E-invoicing applies to this business** — tick this only if the business is above the e-invoicing
   turnover threshold. The software does not guess: turnover is something you know and it does not.
2. **E-way bill threshold** — seeded at ₹50,000. It is a setting rather than something built in,
   because the statutory figure has changed before.
3. **Use the production portal** — leave this off. It stays off until sandbox testing is signed off,
   and the software refuses to switch it on before credentials exist. Production files real invoices
   with the government, and an IRN cannot be withdrawn after 24 hours.
4. **NIC credentials** — enter the client id, client secret, username and password, then
   **Store credentials**. All four are needed; the portal accepts nothing partial.

Two things about credentials that will otherwise surprise you:

- **They are never shown back to you**, not even partly masked. To change them, type all four again.
- **They are encrypted for your Windows login.** If the app is ever run as a different Windows user,
  or the database is restored onto another machine, they must be re-entered. The error message will
  say so.

## Registering an invoice (E-invoicing → E-invoice (IRN))

Only a **posted** tax invoice can be registered — the IRN is computed from its number, and a draft has
none.

1. Pick the invoice from the list. The **E-invoice** column shows whether it already has an IRN.
2. **Preview payload** shows exactly what would be sent, and the IRN that would be computed, without
   contacting anyone. Worth doing on the first few invoices.
3. **Get IRN** (Ctrl+A). On success the IRN, acknowledgement number and signed QR appear.

**If something goes wrong, read the status line rather than pressing the button repeatedly.** There
are three different situations and they need different responses:

| What it says | What happened | What to do |
|---|---|---|
| Registered, with an acknowledgement | Done. | Nothing. |
| Already registered | You had already done it. | Nothing — this is not an error. |
| Not registered, with a portal error code | The portal refused the invoice. | Fix what it names, then try again. Retrying unchanged will fail the same way. |
| No response from the portal | The call did not come back. The invoice **may or may not** be registered. | Just press **Get IRN** again. The software asks the portal what it knows before sending anything, so you cannot create a duplicate this way. |

That last row is the one worth trusting. Pressing **Get IRN** again after a network failure is always
safe — it will never register the same invoice twice.

### Cancelling

Fill in the reason code and press **Cancel IRN**. The button is disabled once the 24-hour window
shown as **Cancellable until** has passed. After that the only route is a credit note, which is an
ordinary accounting entry, not a portal action.

## E-way bills (E-invoicing → E-way bills)

Register the invoice's e-invoice first — bills are generated from the IRN.

1. Pick the document. The pane tells you whether its value obliges a bill. This is **guidance, not a
   block**: the threshold applies to a consignment, and several invoices may travel together, so the
   call is yours.
2. Enter the **distance** and the Part-B transport details.
3. Watch the expected-validity line. **One day per 200 km normally — but one day per 20 km for
   over-dimensional cargo.** Setting the vehicle type to ODC cuts the validity to a tenth, which is
   correct and catches people out.
4. **Generate** (Ctrl+A).

Which Part-B fields you must fill depends on the mode, and the screen says which:

- **By road** — vehicle number.
- **By rail, air or ship** — transport document number and date.

### Afterwards

- **Update Part-B** — the vehicle changed en route. Allowed as often as needed.
- **Extend validity** — enter the remaining distance and a reason. The portal decides the new expiry;
  the software records whatever it grants.
- **Cancel bill** — within 24 hours of issue only.

## Keyboard

The shell shortcuts apply throughout: **Alt+G** Go To, **Esc** back, **Ctrl+A** accept, **Ctrl+Q**
clear. Full map in [keyboard-shortcuts.md](keyboard-shortcuts.md).
