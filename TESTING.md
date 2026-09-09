# Testing the money in ProBooks

    cd C:\Users\sws\Desktop\container
    check.bat            (Linux/macOS: ./check.sh)

That builds the app and runs `tools/MoneyChecks` - a console project that drives the **real services**
(`InventoryService`, `SalesService`, `LedgerService`, `ReportService`, `CashBookService`,
`BuyPlanService`, `ShopExpenseService`) against a **throwaway database in your temp folder**. It never
opens `Documents\ProBooks`. Exit code is the number of failures.

The numbers it expects are worked out by hand, in rupees and paisa, and written into the test. That
is the point: a change in what the app rounds, stores or reports has to fail a check, not quietly
shift a figure on screen.

## What it proves

| Area | The identity being held to |
| --- | --- |
| rounding | half a paisa goes away from zero, everywhere, always |
| the amount readback | a shifted zero never reads back the same words (200,000 random amounts) |
| a bill | `TotalAmount` equals its own line totals minus the discount, and every figure is stored exactly as printed |
| paying | paying the printed bill settles it; one paisa more is refused |
| stock | received − sold + returned is the number left, to the third decimal |
| a corrected cost | the sold lines are re-costed and profit moves by exactly the cost difference |
| returns | a full return credits the bill paisa for paisa; over-returning is refused |
| the customer | bills − payments − returns equals the ledger's own entries |
| the supplier | the bill less every payment, and one cash-book line per payment, never two |
| order sheets | the saved sheet re-opens with the figures the sheet showed, and the tape is the sum of the rows |
| guards | negative costs, zero payments, empty titles, missing suppliers, overselling, discounts larger than a bill |
| the whole database | scanned: no money value anywhere has a third decimal |
| storage size | how large an amount survives in a text column versus a float one |

## What it cannot prove

Anything about the window itself: that a button is where you want it, that a grid column is wide
enough, that the PIN gate asks when it should, that a printed bill looks right, that a backup
restores on your machine, that the numbers you already typed survived the upgrade. Those need eyes -
the list is at the bottom of this file.

## Things the audit found and what was done about them

**Fixed - two rounding rules were in use.** `Math.Round` on decimals rounds to the *nearest even*
figure (267.525 → 267.52) while the app's own formatter rounds half up (→ 267.53). Seven money paths
used `Math.Round`, including the amount a customer is credited when they return goods, so a bill and
the ledger could disagree by a paisa about the same transaction. There is now one rule,
`Money.Round`, and every money figure goes through it.

**Fixed - what was printed was not always what was stored.** A line amount was `quantity × price`
with no rounding, so a sale of 0.375 at Rs 1,450.50 stored 543.9375 while the bill read Rs 543.94 -
and paying that 543.94 was **rejected** as more than the bill. Money is now normalised to the paisa
at every input, a line's amount is rounded where it is defined, and the bill total is the sum of the
printed lines. Reports read the same line amounts, so the shelf, the bill, the ledger and the
reports cannot drift apart.

**Fixed - the order sheet rounded later than the rest.** The sheet's one expense figure and its
yen rate were taken as typed while you worked, and rounded only on the way to the database. A sheet
showing Rs 127,683.495 all-in came back as Rs 127,683.50 after saving, and profit moved with it; a
rate typed as 1.0701234567 priced a row at Rs 171,353.52 while the saved sheet priced it at
171,353.51. Both are normalised where they are defined now, so the tape, the saved sheet, the printed
sheet and the reports are one number.

**Fixed - refusals lied about the quantity.** "Only 0.38 in stock" when 0.375 kg was left (the
quantity formatter showed two decimals) invited you to type 0.38 and be refused again. Stock
messages show three now.

**Not changed - needs your decision.** "Profit" does not mean one thing on every page:

- the containers list and Home: `sold − cost of those goods`. A container's own sea freight, customs
  and clearing are recorded and shown next to it, but **not** taken off that figure;
- the Profit page's daily rows: `sold − cost − shop expenses` (rent, salaries), which does not include
  container expenses either;
- a sale **discount** reduces what the customer pays and what the ledger carries, but not profit on
  any page - it is never allocated to lines.

So on Home, the container that billed Rs 140,543.87 with Rs 200,000 of freight and a Rs 5,000.01
discount reads a profit of Rs 139,849.95, where the money actually left with you is closer to
−60,150.05. The money-checks run prints this as a `note` every time, so it cannot be unlearned by
accident. Tell me which definition you want and I will make every page agree: profit after container
expenses (the way you do the paper sheet: sales − goods − one expense figure) is what I would pick,
with discounts spread across the lines of the bill they belong to.

**Not changed - noted.** Some tables (`SupplierPayments`, `SaleReturns`, `CashBook`, `ShopExpenses`,
`StockAdjustments`) were created with money as SQLite `REAL`, a float, while the model writes money as
exact text. A fresh install gets text; a database upgraded from an earlier release keeps the float
columns. A float holds about 15 to 16 digits, so amounts stay exact up to roughly 90 kharp
(9 × 10¹³) with two decimals - far past any container you will buy - but the check measures it
rather than assuming it. Say the word and I will move those columns to text for good.

## Manual pass, when you have five minutes

1. Type an amount into a supplier bill and watch the words line: `15,00,000` should read "15 lac".
2. Sell a whole container's stock of one item, then return two pieces: stock, the bill's remaining,
   and the customer's balance should all move by the same figure the return credited.
3. Set an item's cost from 0 to the real figure, press Back, and check the Containers list profit has
   moved without clicking the nav item again.
4. Create a container with a part payment, then pay the rest on We owe with a note: the note should be
   on the Main ledger line, and "Paid to this supplier" should show both payments.
5. Sign in as staff: every write button should be dead, and the PIN prompt should appear for the
   writes that are allowed.
6. Print a bill, then restore yesterday's backup in a *copy* of the folder and confirm the figures
   match what you last saw.
