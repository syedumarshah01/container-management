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
| an invoice's standing | the "previous ledger balance" on a printed bill is what their ledger held up to that bill's own line, not today's total with the bill subtracted from it - so re-printing a bill says the same thing about that day whatever has happened since, the money paid after it shows on its own line as today's figure, and the next bill's previous balance carries on from this one through whatever came between |
| stock | received − sold + returned is the number left, to the third decimal |
| a corrected cost | the sold lines are re-costed and profit moves by exactly the cost difference |
| the rate a yen figure is taken at | the rupees on a yen line are exactly its yen figure times the rate kept on it, to the paisa, for an expense *and* for an item's cost price; the rate on the row overrides the container's and then becomes it; a rate of 1 - nothing written at all - is refused; and a rate changed tomorrow re-values nothing that was paid yesterday |
| the freight in the cost | an item's weight in the sum is what a piece weighs times how many were landed; the shipment's expenses - rupees as written, yen converted once at the container's rate, with that rate kept on the line - are added up, divided by that weight and shared back onto the items, so the **shares equal the expenses to the paisa** and the paisa that will not divide goes on the heaviest lot; the per-piece cost then carries what a piece can carry in paisa and the few rupees left over are named on the page, not folded into a price; **an item with no weight stops the sharing for the whole box** rather than leaving its freight on someone else's cost; and deleting every expense brings every cost back to the goods price, the sold lines with it |
| returns | a full return credits the bill paisa for paisa, and over-returning is refused. What they still owe absorbs the return first; only what is left over leaves the cashbook, once per return, with the till line and their ledger line agreeing to the paisa |
| the Main ledger's returns figure | the month's red "goods back" number equals every return credit in the book, and never touches cash in hand |
| the customer | bills − payments − returns equals the ledger's own entries, and every line of their book puts its money in exactly one column of the page and of the paper - a bill, a receipt, goods back, money handed over - so the four columns run the balance and nothing is counted twice or left out |
| a customer's month | the receipts figure a month box shows is the payments dated in that month, and the lines under it are those payments - a payout to them and a return credit are not money collected, so neither is netted off it; five paisa is a month with five paisa in it, not an empty one; and fourteen months walked one at a time add back to the whole book, so no receipt can hide between two months or be counted twice |
| the date on a money line | a receipt, a payout and an expense are stored on the day the form was set to, not the day Record was pressed - the fixtures date money into other months and other years and find it in exactly those columns, never in the current one |
| the year statement | each year's twelve months add back to the year's own line under them - one builder for that line, used by the table and by the print, so a total cannot disagree with itself: cash in less cash out plus what the year brought forward is December's closing figure, a year never reaches into the next January, the value of goods returned is shown beside the cash columns and added to neither, a month with nothing in it is still a row, and the printed statement carries the same figures as the pages it was made from |
| the selling year | a month's profit is its sold money less the cost of those goods, an advance paid without pointing at a bill does not close that bill, and a month whose only movement is a return sells a negative figure - Home's rule, not a second rule invented for paper |
| paying a customer back | a payout moves their ledger and the till by the same figure, never past what their own book says is held of theirs, is never counted as a refund of a bill or as an expense, and reaches their page under "Paid out" rather than under "Sold" |
| the supplier | one cash-book line per payment, never two, and every payment has one |
| the container's "we owe" box | the figure typed there is what We owe shows: money handed over at creation is recorded as a payment on top of it, never netted off it, and paying past the figure is refused |
| order sheets | the saved sheet re-opens with the figures the sheet showed, and the tape is the sum of the rows |
| guards | negative costs, zero payments, empty titles, missing suppliers, overselling, discounts larger than a bill, no arrival date, paying past what a container says is owed |
| the order of the book | the ledger hands its lines over in the order they were made - by day, and within a day in writing order - numbered step by step, each running figure the balance the book had reached; the page shows the newest on top while the printed statement keeps the time order |
| the return outcome | the page states, in rupees and before the button is pressed, what the rule will do - and the figures it names are the posting's own arithmetic, run with writing switched off, so the line can promise nothing the book does not write |
| the arrival date | asked for, never assumed - a container without one is refused, a date in the past is stored as written, and a form that does not show the date cannot clear it |
| the paid box on the import form | raising it adds one payment; lowering it takes the newest payments back; the cash book keeps exactly one line per payment, at the same amount |
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

**Fixed - a reprint of a bill rewrote its own history.** The invoice's "previous ledger balance" was not
read from the ledger at all: it was today's total, less what is left on that bill today. On a bill printed
the day it was written that is the right figure by luck, and the three lines added up - which is why it
survived looking at. Re-print it after the customer pays anything, or after you bill them again, and the
paper says they owed that before the bill existed. The standing now comes from the ledger, cut at the bill's
own line, and what has happened since is a second figure, labelled as today's.

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
discount read a profit of Rs 139,849.95, where the money actually left with you is closer to
−60,150.05. The money-checks run prints this as a `note` every time, so it cannot be unlearned by
accident.

**Fixed - a discount now reaches profit, and it is shared, not guessed.** It used to be that a
discount reduced the bill, reduced the ledger and reduced what the customer paid, but no profit
figure anywhere noticed - while a *return* on the same bill was already credited at the discounted
price. So the two sides of one bill were measured in two different ways. A discount is now spread
across that bill's lines in proportion to what each line was billed for, and reports count the
shared-out figure: on the test bill, the Rs 543.94 line is counted at 524.59 and the Rs 139,999.93
line at 135,019.27, and those add back to exactly the Rs 135,543.86 the customer was asked to pay.
The paisa that does not divide evenly goes on the biggest line, never on the smallest, so no line can
be pushed below zero. A bill with no discount is untouched, to the paisa. Nothing was stored
differently and no old bill was rewritten - this is worked out when a report runs, so history
corrects itself without a migration. Cost is deliberately *not* scaled: a discount is a price
decision, not a cheaper purchase.

**Fixed - an amount list on screen used to round the money for the eye.** The bill grid, a customer's
payments and a container's expenses printed `Rs {0:N0}`, which drops the paisa the book keeps, so those
lines added up to a figure the page did not show and a hand-checked total failed on the screen while the
book was right. They print in the same voice every other figure on the page uses - paisa when there is
any - and they are right-aligned, which is what makes a column checkable by its last digit.

**Fixed - the import form could set the bill below what had been paid.** "We owe" is not a field, it is
the bill minus the payments, and the form used to write the bill without looking at the pile. Lower the
bill past what you had already handed over and the container simply started owing a negative amount. It
refuses now, and names the figure it is refusing.

**Replaced: the container's freight, customs and clearing are now inside the cost of the goods.**
The decision after the audit was that profit meant sold less the goods price, with the shipment's
expenses shown beside it. That is no longer how it works, and this paragraph is here so the change is
visible rather than remembered: each item carries a weight, the expenses on the container are added up
and divided by the weight of everything in the box, and each item's share - by its own weight - is added
to its cost price. Profit on Home, on the containers list and on the Profit page is sold less *that*
cost, so a container that was landed at a loss no longer reports a profit. What follows from it:

- A yen figure is converted once, at the moment it is saved, and the rupee total, the yen figure and the
  rate are kept on the line. Changing the rate later re-values nothing: money already paid to a clearing
  agent is a fact, not a rate.
- **Rs for 1 yen is asked for where the yen figure is typed**, in the expense row and in the item row, and
  it is the same figure the Import details card holds - one rate per container, shown twice, never two
  rates to square up. A yen line typed with a rate in that box takes the rate as written and the container
  then carries it. What the figure comes to in rupees is shown under the box *before* Add is pressed, and
  it is the service's own multiplication, not a copy, so the rupees read on screen are the rupees kept.
- A rate of 1, or no rate at all, is refused for a yen figure rather than believed: ¥180,000 booked as
  Rs 180,000 is the mistake a rate of 1 hides. A rate *below* 1 is the normal shape of this pair (a little
  over forty paisa to the yen), so it is what is accepted - only the untouched default is refused. And if
  the bill was in rupees after all, the box is left alone and Rs (PKR) chosen.
- **A cost price can be typed in yen too.** The item row takes a currency next to the price; the rupee
  figure is what the whole book then works in - freight shared on top of it, stock valued at it, sold lines
  costed at it - while `ForeignCost` keeps what the invoice said and `CostRate` the rate it was multiplied
  by. Those two are kept on the item so the rupee cost can be re-derived to the paisa years later
  (`Rs = ¥ x rate`), which is what the checks assert. Re-saving the item in rupees is how a wrong rate is
  corrected: the figure is then taken as written, with no rate and no conversion to undo.
- An expense's description is a plain text box, not a list. "Demurrage at the port" is a real line in a
  shipment's books and a dropdown cannot be made long enough to hold every port's invention; a blank is
  kept as "Other".
- The cost of a piece is kept to the paisa, like every money figure in this book, so a bill can be
  checked against the cost in the grid. A thousand pieces cannot always carry their lot's exact share
  in whole paisa, so what is left - a few rupees on a large freight bill - is stated on the page instead
  of being hidden in a cost with three decimals.
- The divisor is the goods, not the paperwork: what each lot's pieces weigh, added up. The container's own
  "Weight (kg)" box on Import details is what the shipping papers say the load weighed, and it is
  deliberately not divided by, because a divisor that cannot be traced back to the items cannot be checked
  against them. The tape under the expenses prints the figure that *was* used, so when the two differ it is
  visible on the page rather than quietly changing costs.
- Weight stops being optional for that container's costs the moment it has expenses: if any item in the
  box has no weight, nothing is shared out at all, the expense stays on the container, and the page says
  so. Sharing the unweighed lot's freight onto its neighbours would move prices with no trace.
- Lines already sold are re-costed with the landed figure, as they are whenever a cost is corrected, so
  the profit a container made moves when its freight is entered. Deleting the expense moves it back.

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
3b. Take a bill the customer paid on the spot and return one piece: the Main ledger should have gained one
   outflow of exactly the credited amount, cash in hand should be down by that figure, and their page should
   read Settled rather than an advance. Return a piece of a bill nobody has paid instead and no cash should
   move at all - but it must still show as a red "goods back" figure for that month on the Main ledger, since
   that page answers "how does the book look today".
4. Create a container with a part payment, then pay the rest on We owe with a note: the note should be
   on the Main ledger line, and "Paid to this supplier" should show both payments. The arrival date
   should be blank when the form opens - not today - and Create should refuse until you pick one. Pick a
   date in the past and the list should show exactly that day; a wrong one can be corrected under
   "Edit import details". The figure written in "We owe them (Rs)" is what the We Owe page should show as
   owed - not that figure less the paid-now money, which is the mistake this rule removes.
3c. Type a quantity on a return and one line should say what will happen, in rupees: on a bill they still
   owe, "... is adjusted in their ledger - no cash moves"; on a settled bill, "Nothing is owed on this bill,
   so Rs X is paid out of the cashbook"; on a part-paid bill where the return is bigger than the debt,
   both halves at once. Press it and the till should move by exactly the figure the line named - or not at
   all, where the debt absorbed the return. Their own balance changes only by the relief, never twice.
3d. On a customer's page, the ledger should count up by day and by writing order with a "No." column, and
   the latest entry should sit at the top with the balance the page shows beside it. Print the statement:
   it should read the other way round, opening balance at the top, because that is how paper is read. The
   paper has the same columns as the grid, including "Returned": take goods back on a bill and the statement
   should show a line with a figure under Returned and nothing in Sold, Received or Paid out - a return is
   neither money in nor money out, and before it had its own column the line printed as a row of dashes whose
   only trace was the balance moving. Add the four columns up over the whole page and they should land on
   the balance printed at the head of it, paisa and all.
   "Receive money" has a date box at the head of its row, open on today. Set it to a day in a past month
   and record: the money should appear in *that* month on the Main ledger page and on the year statement,
   the ledger line should read that date with the running figure counting up through it, and the bill
   should be the same amount lighter. Only which month the money moved in depends on the date - the
   balance, the bill and the words under the box are all untouched by it.
   The Payments card has a month box by its "Received" figure. Leave it on "All months" and the figure is
   everything the book holds from them; pick a month and both the figure and the list narrow to it, so the
   lines under the number *are* the number - add them and it should land on it to the paisa, paisa shown.
   Take a month in which you paid them something back or they returned goods: "Received" should read
   Rs 0, because neither of those is money collected. A month with no receipts should show a figure of zero
   and no rows, not vanish from the box - and it is there, even for the current month, even for a customer
   you have never been paid by. Record a payment dated in another month while the box is on one, and the box
   should move to the month the money was written into, so a save never looks like a loss.
4b. Pay the container down to nothing on the We Owe page and it should read settled; try one paisa more
   and it should be refused, naming the figure that is owed. A container left over from before this rule,
   with money paid past its figure, shows nothing owed and says so in one line on the container form.
4c. On the We Owe page, a customer whose own ledger runs minus should be under "Customers we owe" - an advance
   they left sitting with you, or the cash half of a return on a settled bill. Pay part of it: their page
   should move towards nothing by exactly that figure, one line should stand in the Main ledger as money out,
   and cash in hand should be lighter by the same amount - and the payout should read "Paid out" on their
   ledger, never "Sold". Try one paisa past what their book holds and it should be refused: a pay form must
   not be able to create a customer who owes the shop. Home's profit should not move at all, because handing
   someone their own money back settles a debt, it is not an expense.
4d. Open **Year statement** and pick a year the shop traded in. The page is three tables and no prose, and
   each table shows all thirteen of its lines at once - the year's own line is the last one in the card, not
   something below a scroll bar. It is headed "Total {year}" in all three tables, including the main ledger,
   where a bare "December" would read as a thirteenth month; it is tinted and set bolder than the months.
   Add a column's twelve months by hand and the figure at the foot should be it. The "After costs" figure at
   the top should be the last line of the Sales table less the last line of the costs table, which is the
   only arithmetic on the page that crosses two tables. The till's December closing should equal what the
   Main ledger page holds if the year is the current one, and should *not* if it is a year in the past -
   that difference is what a closing balance is for. A month with nothing in it should be a row of dashes,
   not a missing row. Press Print and the paper should carry the same figures, the same twelve-plus-total
   shape, and one line saying what the year was carrying when it opened.
4e. On a container, type a weight (kg each) on two items and enter a freight expense: the line under the
   expenses list should read "Rs X over Y kg = Rs Z a kilo", and each item's cost should have risen by its
   own kilos times that Z, divided by how many pieces were landed. Do it on paper for one item and it
   should come out the same to the paisa, with the paisa that would not divide sitting on the heaviest lot.
   Enter an expense in ¥ and the rupee it comes to should be shown *before* Add is pressed and kept on the
   line afterwards; change the rate afterwards and that line should not move. Put a third item in with no
   weight and the whole sharing should stop - costs back at the goods prices, and the page saying one item
   has no weight - and weigh it to see every item in the box re-costed over the new total. Then delete the
   expenses one by one: the costs, and the sold lines from those lots, should come back to the goods prices
   paisa for paisa.
4f. Choose ¥ in the expense row and the *Rs for 1 yen* box should appear there with the container's rate in
   it, together with the rupee the figure comes to - before Add, and the same figure on the line afterwards
   under "Entered as". Leave the rate at nothing (or 1) and Add should refuse, saying why, rather than
   booking ¥180,000 as Rs 180,000; write a rate of 0.42 and it should be accepted as it stands, because a
   yen is worth less than a rupee. Then type a cost price in ¥ on an item: the grid's cost column should
   carry the rupee figure with the invoice's yen figure under it, the item's own price box should show the
   yen figure back when the row is selected again, and multiplying it by the rate on the line should give
   the rupee cost to the paisa. Change the container's rate the next day and neither that item's cost nor
   that expense's rupees should move - only the next figure typed picks up the new rate.
5. Sign in as staff: every write button should be dead, and the PIN prompt should appear for the
   writes that are allowed.
6. Print a bill, then restore yesterday's backup in a *copy* of the folder and confirm the figures
   match what you last saw.
6b. Print a bill you wrote *last month*, then take a payment against it and print it again. Its "Previous
   ledger balance, as at ..." line and its "This invoice balance" should not have moved a paisa: they are
   statements about that day. What should move is the muted line at the bottom, "Outstanding on their book
   today", which only appears once anything has happened since the bill - a bill printed on its own day has
   one total and no such line. If the first two had moved, the paper would be claiming that money was owed
   before the bill was written, and its arithmetic would still have looked right, because the figure was
   found by subtracting one today's number from another.
7. On a container's **Edit import details**: one line per field, label at the left, the words and the
   "still owe" figure after the box, nothing overlapping - a grid cell that was never named puts two
   inputs on top of each other, and the app compiles and passes every money check regardless. The two
   rupee boxes should not have spinner buttons, and the mouse wheel over them should do nothing; the
   weight box should still step. Cartons and CBM are not in this form any more - they keep whatever the
   container was created with, and a check makes sure saving here does not erase them. Raise
   "Paid (Rs)" by 1,000 and save. We Owe should
   show one more payment, dated today, marked "Recorded on the container form", one more line in the cash
   book, and the owed figure exactly where the "We owe (Rs)" box says it is. Type the old figure back: that payment and that cash line
   should disappear, and the trimmed neighbour should read exactly what it did before. Then try to set
   "We owe" to 0 while payments stand against the container: the container settles, it does not refuse -
   "We owe" here is what is left to pay, and the bill is that figure plus every payment. The words line
   and this box must say the same amount, since both are the same money.
