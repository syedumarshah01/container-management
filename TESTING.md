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

`CHECKS.md` is the map of that run - which of the app's methods it drives, what is deliberately *not*
proven by it (a screen's layout, a backup's files, the browser a print opens), and the rules a new check
has to follow to be worth adding.

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
| a corrected cost | an item's cost price corrected after it was sold re-costs the sold lines *and* the return lines of that lot, so the container's profit, Home's month, Home's whole-book card and the Profit page all move by what the sold pieces carry - the stock on the shelf is valued at the new cost too - while the price the customer was billed and what they owe stay exactly where they were, because a cost is not a price |
| the freight in the cost | an item's weight in the sum is what a piece weighs times how many were landed; the shipment's expenses - rupees as written, yen converted once at the container's rate, with that rate kept on the line - are added up, divided by that weight and shared back onto the items, so the **shares equal the expenses to the paisa** and the paisa that will not divide goes on the heaviest lot; the per-piece cost then carries what a piece can carry in paisa and the few rupees left over are named on the page, not folded into a price; **an item with no weight stops the sharing for the whole box** rather than leaving its freight on someone else's cost; and deleting every expense brings every cost back to the goods price, the sold lines with it |
| returns | a full return credits the bill paisa for paisa, and over-returning is refused. What they still owe absorbs the return first; only what is left over leaves the cashbook, once per return, with the till line and their ledger line agreeing to the paisa |
| the Main ledger's returns figure | the month's red "goods back" number equals every return credit in the book, and never touches cash in hand |
| the customer | bills − payments − returns equals the ledger's own entries, and every line of their book puts its money in exactly one column of the page and of the paper - a bill, a receipt, goods back, money handed over - so the four columns run the balance and nothing is counted twice or left out |
| a customer's month | the receipts figure a month box shows is the payments dated in that month, and the lines under it are those payments - a payout to them and a return credit are not money collected, so neither is netted off it; five paisa is a month with five paisa in it, not an empty one; and fourteen months walked one at a time add back to the whole book, so no receipt can hide between two months or be counted twice |
| the landed count on an item | the Qty box on an item's form is how many came in, and Save moves it: correcting 1,000 to 700 re-shares the container's expenses over 700 pieces, so a piece's freight doubles from Rs 0.70 to Rs 1.00. The shelf count in the other box moves the stock and nothing else, and stock above what landed is refused rather than written |
| Home's dates | the two boxes at the head of Home belong to the book card and to nothing else: a range wide enough to hold the bills sees the whole book's sales, profit and market figure, a year with nothing in it says zero on every figure including the containers counted, a range left open at the far end has run to today, and one typed the wrong way round reads its own first day rather than answering with nothing. A container's own count is by the arrival date written when it was booked, so a lot that has only landed is in the period it landed in - sold or not - and is not in the week before it. "This month" under the card takes no dates at all - the page keeps one figure that needs no reading of the headings first - and stock stays the shelf as at today, which the card says out loud |
| the customer's own details | name, mobile, address and note are editable on the customer's page, because the number there is what the WhatsApp button dials: a SIM that changed should not put the page out of use |
| the date on a money line | a receipt, a payout and an expense are stored on the day the form was set to, not the day Record was pressed - the fixtures date money into other months and other years and find it in exactly those columns, never in the current one |
| the year statement | each year's twelve months add back to the year's own line under them - one builder for that line, used by the table and by the print, so a total cannot disagree with itself: cash in less cash out plus what the year brought forward is December's closing figure, a year never reaches into the next January, the value of goods returned is shown beside the cash columns and added to neither, a month with nothing in it is still a row, and the printed statement carries the same figures as the pages it was made from |
| the selling year | a month's profit is its sold money less the cost of those goods, an advance paid without pointing at a bill does not close that bill, and a month whose only movement is a return sells a negative figure - Home's rule, not a second rule invented for paper |
| paying a customer back | a payout moves their ledger and the till by the same figure, never past what their own book says is held of theirs, is never counted as a refund of a bill or as an expense, and reaches their page under "Paid out" rather than under "Sold" |
| the supplier | one cash-book line per payment, never two, and every payment has one |
| the container's "we owe" box | the figure typed there is what We owe shows: money handed over at creation is recorded as a payment on top of it, never netted off it, and paying past the figure is refused |
| order sheets | the saved sheet re-opens with the figures the sheet showed, and the tape is the sum of the rows |
| the printed order sheet | the paper carries each row's own figures, its bills and the sheet's seven-figure summary - nothing is worked out again for the printer, and no sentence explains anything; a yen bill prints in yen with the rate *that row* was taken at; the rows' profit and the summary's differ by exactly the bills; and no money figure on the paper has a third decimal |
| a sheet's expenses | each bill is a row - what it was for, how much, in yen or rupees - and the sheet's expense figure is those rows added up, never a number typed beside them; a yen bill keeps its own rate so re-saving the sheet cannot re-value it, and a bill typed after the rate moved is taken at the new one |
| the home page's figures | the five are read off the book instead of worked out again beside it: stock is what is left at landed cost, and what is out there on the containers' goods is the same sum as what the bills still owe - one counted by container, one by bill, which is the pair that would first show a rupee going missing. A corrected item cost moves the card's profit and stock figures with the container's page, while its sales figure does not stir, because a cost is not a price |
| money received is against a named bill | the bill offered to be settled is stated with its number, its date, what is left on it and which container the goods came out of; the amount it names is the same subtraction the bill's own page runs, a settled or cancelled bill drops out of the list so it cannot be picked again, and money past what is left on the chosen bill is refused rather than split |
| a bill edited, a receipt deleted | the bill, the customer's ledger, the shelf, that lot's cost and profit, and Home all move together or none of them do; an edit **rewrites** the receipt written against the old bill instead of adding a second one; and a deleted receipt takes its till line and its ledger line with it, so cash in hand cannot keep counting money that is gone |
| a bill with history under it | a bill with a return on it is neither editable nor cancellable, and a bill from yesterday is not editable at all - it is cancelled and written again - so nothing is ever rewritten under a figure a customer has already been told |
| a sale cancelled | the pieces go back on the shelf, the money handed over goes back out of the till on a line of its own, the bill is kept and marked cancelled instead of deleted, and cancelling it a second time is refused |
| the till's opening figure | saying it again corrects it - cash in hand moves by the difference and not by the whole new figure - one opening line is kept, and taking it back to nothing leaves no empty line behind |
| an expense corrected | Home's month profit moves by exactly the change, the till keeps one line at the new figure with the shop's own words for the description, a blank description or a zero amount is refused, and deleting it puts the whole figure back once |
| a shelf re-counted | the count moves the stock and what it is worth, and no money already earned; the record keeps what it moved from, to, and why; a count above what was ever bought, or below zero, is refused |
| a container closed | nothing moves: its sales, its profit and what is still out there stay where they were, and the box stays in the lists the pages are added up from, because a closed box is still owed to and still pays |
| the lists, against the rows under them | To collect shows each customer the figure their own page holds and leaves out anyone even or holding money back; the inventory page is the shelf added up and worth what Home's stock line says; the sell page offers exactly what the shelf holds; correcting a customer's name or number moves no money at all |
| every row, swept | over the whole fixture shop: each bill's lines less its discount are its total, each customer's ledger adds back to their bills, receipts, returns and money handed over, each lot's shelf is what came in less what went out plus what was handed back and any count made since, what We Owe per container is that container's bill less the payments on it, and the till's own lines add to the cash in hand shown |
| a container's money | sold = collected + in the market, on every lot; a bill drawn from one container gives that container the whole of its outstanding with nothing shared, a bill across two lots is shared by what each was billed for with the odd paisa on the bigger share - so the lots add back to the bills exactly; and a return moves the sold and market figures without touching the money that came in |
| guards | negative costs, zero payments, empty titles, missing suppliers, overselling, discounts larger than a bill, no arrival date, paying past what a container says is owed |
| the order of the book | the ledger hands its lines over in the order they were made - by day, and within a day in writing order - numbered step by step, each running figure the balance the book had reached; the page shows the newest on top while the printed statement keeps the time order |
| the ledger in a chat message | WhatsApp carries the ledger itself - the same lines, the same order and the same words as the printed statement - closing on the balance at the head of the page, in figures and in words together; a book too long for a link loses its oldest lines and says how many, and a number that cannot be dialled is refused with the digits it found |
| a message the shop typed | what is typed in Settings goes exactly as it was typed, with the book's figures only where {name} {shop} {date} {balance} {words} {ledger} were put and no statement added that nobody asked for; a word the book cannot fill is refused when the settings are saved, not in front of a customer, and the link limit falls on the ledger block only - a shop's own words are never cut off |
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

**Changed - an order sheet's expenses are typed a bill at a time.** The sheet used to hold one number for
freight, customs, clearing and labour together, typed into a box beside the rate. That box is gone: the
Expenses card on the sheet works the way a container's does - what it was for, the amount, the currency, and
for a yen bill the rate it is to be taken at - and the sheet's expense figure is those rows added up,
recomputed on every save. Two things follow, and both are the point of the change. The first is that there is
no longer a total that can disagree with the lines under it, which is what the old shape got wrong: a typed
total and a list of bills could each be right on their own day and contradict each other the next. The second
is that a yen bill on a sheet behaves like one on a container: it is converted once, the yen figure and the
rate are kept on the row, and the row's note reads back `¥180,000 at 1.0701 = Rs 192,618`. Saving the sheet
again with that row untouched puts the same rupees down; only the next bill typed follows the rate if the
rate has moved. A sheet left at a rate of 1 refuses a yen bill rather than booking ¥180,000 as Rs 180,000,
and a bill with no amount is refused too, mid-save, with nothing written.

Sheets made before this change keep their figure: the first time the book is opened after upgrading, a sheet
holding a typed total of, say, Rs 45,000 grows one row reading "Other" for Rs 45,000, so nobody opens an old
sheet to find its expenses missing, and the total is unchanged. From then on it is the rows that decide it.

The rate box for a bill is the row's own (`Rs for 1 yen` under the amount), not the box at the top of the
page: the top box prices the *goods*, and clicking an old bill should not quietly re-price the whole sheet.

**Printed.** A sheet prints on the same paper as the customer's statement, in three tables and nothing else:
the eleven columns the sheet has (item, quantity, yen each, yen total, cost each, cost total, kilos each and
in all, selling each and in all, and the row's profit *before* bills), then the bills, then the summary - the
same seven figures the page carries across its top, in the same words and order. Nothing is recomputed for the
printer: every figure is read off the row, the bill or the total it belongs to, which is the only way the paper
cannot say something the screen never did.

There is no prose on it. What a reader needs to check the money is in the money: the rate the yen was taken at
is the line under the title, each bill's own rate is inside its own line (`¥180,000 at 1.0701 = Rs 192,618`),
and the goods column is headed *before bills*, which is why its total is not the summary's profit. Pressing
Print while the sheet has unsaved typing saves it first, as Back does - a printout that disagrees with the book
is the worst of both.

**Fixed - the order sheet rounded later than the rest.** The sheet's expense figure and its
yen rate were taken as typed while you worked, and rounded only on the way to the database. A sheet
showing Rs 127,683.495 all-in came back as Rs 127,683.50 after saving, and profit moved with it; a
rate typed as 1.0701234567 priced a row at Rs 171,353.52 while the saved sheet priced it at
171,353.51. Both are normalised where they are defined now, so the tape, the saved sheet, the printed
sheet and the reports are one number.

**Fixed - refusals lied about the quantity.** "Only 0.38 in stock" when 0.375 kg was left (the
quantity formatter showed two decimals) invited you to type 0.38 and be refused again. Stock
messages show three now.

**Changed - Home shows the whole book, not only the month.** The page used to carry this month's sales and
profit, a daily list and the backup line, while the figures for the book as a whole were built in
`GetDashboardAsync` and read by no page at all. They are on the page now, and two of their names mean something
plainer than they did: *expenses* is the shipments' bills and the till's own added together, with both halves
shown under it, and *in the market* is what the containers' pages call In the market - the money out there on
their goods - rather than the sum of the customers' positive balances, which is a wider question and is kept in
the model under its own name. What is owed on bills is measured by the one formula a bill is measured by
everywhere, so the list of bills needing attention totals to the same figure the card above it shows.

**Changed - Home shows the whole book, not only the month.** The page used to carry this month's sales and
profit, a daily list and the backup line, while the figures for the book as a whole were built in
`GetDashboardAsync` and read by no page at all. Five of them are on Home now - containers, sales, what is
receivable in the market, stock value, profit - each one the figure the page behind it already shows, added up
once rather than worked out again beside it. Purchases and expenses are not on it: We Owe holds what the
suppliers were billed, and the Expenses page holds the till's own bills, and a total taken from both on a page
that shows neither would be a figure with nothing to check it against. They stay in the model, where the checks
keep their sums honest.

*In the market* means what the containers' pages mean by it: the money out there on their goods, measured by the
one formula a bill is measured with everywhere. It is deliberately not the sum of the customers' positive
balances, which also hold advances and payouts - that figure is kept beside it under its own name, on To
collect, where both are now visible together.

**Changed - money received has to name the bill it settles.** The form used to open with "Not against a
specific invoice" already picked, so a receipt recorded without a second thought settled nothing: the money
was honest, the customer's balance moved, and the bill stayed out there with the same amount on it while the
advance sat unattributed - which is also money that belongs to no container's figures. The box now opens
empty, Record refuses while nothing is chosen, and holding money as an advance is a line that has to be
picked on purpose. What the list offers is read off the book rather than summarised: the number, the date,
what is left, and the container the goods came out of by name and number, so the figure being settled can be
seen before it is committed to. A pick survives a reload only while that bill is still owed on, and the box
empties after a record, because the next receipt is a separate decision.

**Fixed - an item's Save ignored the quantity box.** Saving an item took its landed count from the grid row
and never from the box beside it, so correcting a miscount - 700 came in, not the 1,000 that was written -
did nothing at all: the number snapped back and the freight stayed shared over the wrong count. Before that
it was worse, not better: the count was overwritten by whatever the stock box said, so a shelf count of 950
quietly rewrote what the container had imported. Now the Qty box is heard and the stock box is not, which is
the only arrangement that keeps the freight honest: expenses are divided over the pieces that came in, so a
corrected count *should* re-share them, and a re-count of the stack should not.

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

**A container's money, and one container per bill.** The sell page has a *Container* box beside the search:
choose a lot and only that lot's goods are offered, and picking an item sets the box to its container on its
own. Once the bill has a line, the box shuts at that container: the search offers nothing else, and another
lot's item - a row left in a list typed a moment earlier - is put back with the bill's own container named as
the reason. Removing that container's last line opens the box again. That is the rule rather than a
convenience: the lines are what say where a bill's goods came from, so the box, the search, the money below
and the printed invoice are all made to agree with them, and one is never allowed to be edited out of line
with the others. "All containers" is what the box says while the bill is empty - and while an older bill
being edited turns out to span two lots, which is left as it was found rather than pinned to either half.

Each container then carries three money figures, and they are one subtraction apart: what its goods brought
(sold, after the discount share and any returns), what is still out there (in the market), and what has
arrived (collected). The market figure is built from the bills themselves - each bill's outstanding is
calculated by the same formula the bill's own page and the customer's ledger read, then shared across the
containers its lines came from in proportion to what each was billed for, with the paisa that will not divide
going on the bigger share, as a discount's does across a bill's lines. Collected is sold less that, so the
three cannot disagree and no rupee is invented or dropped by the sharing; the checks add the lots back and
compare them to the bills.

What that definition means where money has moved oddly, stated plainly: a return credited against a bill
lowers the sold figure and the market figure together, so the collected figure stays exactly where the cash
arrived - which is the point, since the question is how much of a lot's money is in the market and how much
has come in, not how the customer's ledger happened to be netted. A bill cancelled after the sale is excluded
with the bill itself, and money received that was never pointed at a bill belongs to no container until it is.

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
   Above that row, the bill being settled is named before it is recorded: the box opens with nothing chosen,
   and each line says the invoice number, its date and what is left on it, with the container's name and its
   number under that. Press Record with nothing picked and it is refused; "No bill - take it as an advance" is
   the last line, and taking it is the only way money is held against no bill. Paying a bill down to nothing
   should leave it out of the list altogether, and typing one paisa more than is left on the picked bill
   should be refused with the amount that is left named back - the money is never quietly moved onto another
   bill or held back from the one it was aimed at. After a record, the box and the amount are empty again.
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
   The columns are read down, so on the page the figures and the words naming them both sit in the middle of
   their column - including the total line, which stays tinted and bolder than the months so it does not read
   as a thirteenth month. The paper keeps its figures right-joined under their headings, because a column of
   amounts is added up on paper the way it is added up in a ledger; only the page was asked to centre.
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
4f0. On an order sheet, the TOTAL EXPENSE box should be gone and an Expenses card should sit under the
   goods rows. Type two bills - one "Sea freight" in ¥ and one "Local clearing" in Rs - and the card should
   read the rupees the yen bill comes to *while* it is being typed, using the rate in its own `Rs for 1 yen`
   box. Save, close the sheet and open it again: the yen row should show the rupees it added with `¥180,000
   at 1.0701 = Rs 192,618` under them, its amount box should hold the *yen* figure and not the converted one,
   and the "+ EXPENSE" figure at the top should be exactly the two rows added. Change the rate at the top of
   the sheet to something else and save: the yen bill should not move, while a bill typed after that is taken
   at the new rate. Then delete one row: the expense figure should fall by that row alone, to the paisa.
4i. On a container's page, add an expense and watch the form when the row appears: the description, the
   amount, the notes and the currency should be empty again, and the date back on today - while the rate box
   keeps the number it had, because that is the container's rate and not part of the entry you just made.
   Pick a row, change it, press Save: the boxes should stay as they are, since the row is still selected.
   Remove a row: the boxes should let go, since the figures in them belong to a line that no longer exists.
   The order sheet's Expenses card behaves the same way, on purpose.
4j. Pick an item row on a container's page, change Qty landed from what it says to a smaller number, and
   press Save: the Purchased column should show the new number and stay showing it after the page reloads.
   Watch "Cost each" - the freight part should move in the opposite direction, because the same expenses are
   now divided over fewer pieces. Then change only "In stock (when editing)" and the cost should not move at
   all; set the stock above what landed and Save should refuse in words.
4h. On the sell page, choose a container in the box beside the search and confirm the item search offers
   only that lot's goods; pick an item and confirm the box moves to its container by itself. Add the line and
   confirm the box is grey with that container in it: try to change it, then search another container's item
   and confirm the pick is refused in words that name the bill's own container. Remove the line and confirm
   the box opens. Bill from one container and leave Rs 2,000 unpaid: that container's page should read
   "Collected" and "In the market" as what you paid and what you left, with no calculator work, and the
   containers list should show the same two figures. A bill across two containers cannot be typed any more,
   so the sharing that attributes a mixed bill's money is checked by the harness rather than by hand - it
   still matters to any bill already in the book. Print the bill from the single-container sale and confirm
   the invoice names the container under the customer's name.
4g. Press Print on an order sheet carrying at least one bill in ¥ and one in Rs. The eleven columns should
   match the grid figure for figure, the bills should print with the rate each was taken at, and the two profit
   figures - the rows' total under "Profit, before bills" and the summary's Profit - should differ by exactly
   the bills. The summary across the bottom of the paper should read the same seven figures the top of the page
   shows, and there should be no explanatory sentences anywhere on it. Then add a bill, print again without
   pressing Save, and confirm the paper shows it (the save happens under the button). A sheet with no bills
   should print the bills table empty, with Rs 0 under it.
4f. Choose ¥ in the expense row and the *Rs for 1 yen* box should appear there with the container's rate in
   it, together with the rupee the figure comes to - before Add, and the same figure on the line afterwards
   under "Entered as". Leave the rate at nothing (or 1) and Add should refuse, saying why, rather than
   booking ¥180,000 as Rs 180,000; write a rate of 0.42 and it should be accepted as it stands, because a
   yen is worth less than a rupee. Then type a cost price in ¥ on an item: the grid's cost column should
   carry the rupee figure with the invoice's yen figure under it, the item's own price box should show the
   yen figure back when the row is selected again, and multiplying it by the rate on the line should give
   the rupee cost to the paisa. Change the container's rate the next day and neither that item's cost nor
   that expense's rupees should move - only the next figure typed picks up the new rate.
4m. Home has two date boxes at the top of the page, over both cards: set From and To around one trading day
   and press Apply. The book card should read "In the period", the line under the title should name those two
   dates, and its sales, market figure and profit should be that day alone. A container booked on one of those
   dates should be counted on it even with nothing sold or spent on it, and should not be counted for a week
   before it landed. The month card under it should not
   stir - it is this month whatever the boxes say - and neither should the stock figure, which the line under
   the card says is the shelf as at today. Clear one of the two boxes and Apply again: it should read "from" or
   "up to" and run open on the missing side. Press "Whole book", which appears only once a date is set, and the
   page should land back exactly where it opened.
4n. On a customer with bills open, press WhatsApp. A chat should open with their ledger typed out in it - the
   lines in the order the money moved, each one's balance, and the total owed at the end with its words beside
   it - and the status line at the bottom should name the number that was dialled and how many lines went in.
   Nothing is attached and nothing is sent by itself: the last press is yours, in the chat. A customer with no
   number saved, or one that is not a mobile, should say so in red at the bottom of the window rather than
   sit there doing nothing - their page's "Details" card is where the number is put in or corrected, and Save
   there is enough; the page does not have to be left. That card is shut by default, and it should open itself
   when a send fails because there is no number saved at all.
4o. In Settings, type a WhatsApp message using the words it lists - "{name}, you owe {balance} ({words})" -
   and Save. A token spelled wrong should be refused on the spot, with the list of what the book can fill. Save
   a good one, then open a customer and press WhatsApp: the chat should carry exactly what was typed, with
   their name and their figure in place of the braces and nothing added. Empty the box, Save, and it is the
   ledger itself that goes, as in the step above.
4k. On Home, the "Whole book" card holds five figures: containers, sales, what is receivable in the market,
   stock value, profit. The market figure should be the containers list's "In the market" column added up, and
   To collect should show the same total on its container box when that box is on "All containers" - those two
   pages read one sum, so if they ever differ, money has stopped reaching a container and it is worth chasing
   the same day. Then tweak the cost price of an item that has been sold: the container's page, this card and
   the Profit page should all move by the same amount, the stock figure should rise or fall by what is left on
   the shelf times the change, and the bill the customer was given should not change at all.
4l. On To collect, the box at the head of the page picks a container and shows what is still in the market on
   it. The figure should be word for word what that container's own page says under "In the market" - open both
   and compare, paisa and all - and the container owing most should be at the head of the box, because this is
   the page that asks who to chase. "Customers owe in total" beside it is a different question: their ledgers
   also hold advances and payouts, so the two figures need not agree and neither is wrong. Put the box on "All
   containers" and only that total is shown.
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
