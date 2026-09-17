# What is new in ProBooks

The newest entry is what the shop's **Settings → Updates** card shows when an update is waiting, so keep the
`## version` headings first and the lines under them in the words a shop reads. One entry per release; the
date is the day it was pushed.

## 1.1.0

- A container page has a **Return to supplier** section under its expenses: pick the goods line, type how many
  units went back, and the units leave the lot and the shelf. What they were bought for comes off what you owe
  that supplier on the lot - the freight they carried does not, because that money was spent on the shipment and
  now falls on the units that stayed. Nothing is typed for the money: it is the cost on the line, so a return can
  never be made to say a different figure from the goods it is about.
- When a return is worth more than the lot owes, the leftover becomes money the supplier owes you, listed on
  **We owe** under **Suppliers who owe us**. Take it in there when it arrives and it lands in the till as money
  in - on Main ledger as IN, on the date you type - and it is never counted as a sale or as income.
- Both are reversible in order: a receipt can be taken back out, and a return only once no money received counts
  on it. The lot's paper carries what went back as its own table, and so does the We owe page.
- A finished lot can be **put aside**. Closing a container takes it out of the Containers list, and *Put away*
  beside the Print button shows the ones that are set aside, with **Re-open** to pull one back. Reports, the till,
  what you owe them and what they owe you are untouched by closing - the lot is out of the way, not out of the
  book, and nothing is counted twice or dropped.
- The *Suppliers who owe us* card on We owe is a heading with its total, the table of who owes what, and a single
  row of boxes that appears only once a supplier is picked in that table. There is no second picker beside the
  table, no figure of its own repeating what a column already says, and no empty form sitting under a list waiting
  to be told who it is for; the amount box's unit is read off the words under it and its ceiling off the line
  above it, in the same shape as the receive-money row on a customer's page.
- Home's date boxes moved to the top of the page and now change the whole-book figures. This month stays this
  month, whatever the boxes say.
- A container is counted in the period it landed in, sold or not, by the arrival date written when it was
  booked. A week before it landed, it is not counted.
- Stock value on Home is what is on the shelf today, and the card says so instead of leaving the date boxes to
  imply otherwise.
- The customer's page has a Details box - name, mobile, address, note - folded away by default, and it opens
  itself when a send fails because no number is saved.
- The WhatsApp button sends a customer's ledger: their own lines, in the order the money moved, each with its
  balance, ending on what is owed in figures and in words together. A number that cannot be dialled is refused
  with the digits it found; where no chat can be opened, the message is put on the clipboard instead.
- Settings can hold a message of your own. What you type is what goes, with the book's figures only at
  {name} {shop} {date} {balance} {words} {ledger}, and a word the book cannot fill is refused when you save.
- An error at the bottom of the window reads as an error, so a refused send is no longer a dead button.
- Print on thirteen more pages - containers, a container's goods and bills, bills, customers, to collect, we
  owe, the till's bills, stock, profit, an item's sales, the main ledger, the order-sheet list and Home. Each
  sheet is built out of the words the page is already showing, so paper and screen cannot tell two stories.
- A new **Reports** page. One list of the reports the book can make - the whole book as it stands, a stretch
  of days, the till month by month, sales month by month, the shop's own bills, containers, who owes, stock and
  profit by item - and one report on the screen at a time, chosen from that list. The date boxes appear beside
  the reports that can be read over them and not beside the others, and the button that clears them says where
  it lands: this month, this year, or the whole book. Print writes the report on the screen, so a sheet in a
  drawer says what it was.
- The **Year statement** and **Profit** pages are gone. Their tables are the Reports page's Main ledger, Sales,
  Expenses, Containers and Profit by item, so the month-by-month figures live in one place instead of three,
  and the year's line at the foot of each table is shaded and bolder than the months, so it cannot be read as a
  thirteenth month and counted twice. Profit's two Excel/CSV sheets are on the Reports page now, beside Print.
- Sales, containers, to collect and both payment forms on We Owe have a box to search. Typing narrows what you
  can see and moves no figure: the money in the cards above a list is read before the search runs, and a payment
  picker keeps the supplier or customer it was pointed at even while you type past them.
- To collect now shows two figures for the container you pick: what has been collected on it, and what is still
  in the market - the same two words the containers list uses, so one figure is not called three things.
- Every bill on a container is on its expenses list, in the currency it was taken in, with the yen rate beside
  it; correcting an item's cost price moves that lot's sold lines, its returns and the profit figures with them,
  and leaves the price the customer was billed where it was.
- Cash in hand on the main ledger is a month, not the book, and the card names the month it closes and what the
  month before handed over.
- Boxes and their captions on a line now stand on that line: a date box beside a button or a heading used to
  sit half a row above it, on every page that has one.

## 1.0.0

- The book as it was released: containers and their landed costs, selling against a lot, the customer ledgers,
  the till, We Owe, order sheets in yen, stock, reports, backups to a folder or Google Drive, and printing.
