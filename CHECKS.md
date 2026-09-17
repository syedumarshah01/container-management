# What the money checks prove, and what they cannot

`check.bat` (Linux/macOS: `./check.sh`) builds `tools/MoneyChecks` and runs it. Today that is **471
assertions over 77 of the app's 118 service methods**, in 61 named groups, against a throwaway database in
your temp folder. `Documents\ProBooks` is never opened, no browser or printer is launched, and the exit code
is the number of failures.

This file is the map: what is proven how, what is *deliberately* not proven here, and the rules a new check
has to follow to be worth adding.

## The sections

| Section | What it holds to |
| --- | --- |
| `FormatRules` | the one rounding rule (half a paisa away from zero), and that a figure never reads back as the same words when a zero has shifted |
| `Flows` | a container from the bill written on it through the sale, the payment, the return and the payout; the ledger's order; the chat message built from a customer's ledger; the range on Home; profit following a corrected cost |
| `Reconciliation` | **what happens to a figure after it is written** - a bill edited, a receipt deleted, an expense corrected, a shelf re-counted, a container closed, a sale cancelled - and the same lists the pages are built from, reconciled row by row |
| `YearStatement` | one builder for a year's total line, used by the table and by the print, so a total cannot disagree with itself |
| `MonthReceipts` | a month's receipts on the customer's page, the till and the printed year statement, all the same money |
| `InvoiceStanding` | a printed bill's "previous balance" read from the ledger at that bill's own line, so reprinting cannot rewrite history |
| `FreightSplit` | the expenses shared by weight into a piece's cost: shares equal the bills to the paisa, the odd paisa on the biggest lot, and an unweighed item stops the sharing for the box |
| `SupplierDue` | goods handed back to a supplier: the units leave the lot and the shelf at the cost on the line and nothing else, the credit settles the lot's bill before it becomes money due back, the bill is never pushed below what was paid on it, the freight the returned units carried falls on the units that stayed, the profit row does not move when nothing sold, a receipt in from the supplier lands in the till as money in and nowhere as a sale, neither a return nor a receipt can be undone in the wrong order, and a table another version left in the file under the same name is refused at startup by name rather than surfacing as a missing column on a page |
| `PrintPaper` | the paper is the screen, said again: cells copied and never recomputed, names escaped so a table cannot be broken by an ampersand, totals last and marked, and every Print button matched to a print command and back - across the whole source folder, not one page at a time |
| `Updates` | the decision an update makes, with no network in it: versions compared by numbers, the folder's own state deciding what the button may do, and the script that does the work held to a list of what it must say and what it must never say |
| `Storage`, and the exactness sweep | no money column anywhere holds a third decimal, and no figure is stored differently from how it is printed |

`Reconciliation` ends with the sweep that matters most on its own, because it does not trust any page: over
the whole fixture shop, each bill's lines less its discount are its total; each customer's ledger adds back
to their bills, receipts, returns and money handed over; each lot's shelf is what came in less what went out
plus what was handed back and any count made since; what We Owe per container is that container's bill less
the payments made on it; and the till's own lines add to the cash in hand the shop is shown.

## Not proven here, on purpose

| Area | Why not, and where it is looked at instead |
| --- | --- |
| Every screen: layout, wording, what a picker shows | XAML only fails when it runs. `TESTING.md` is the walk-through for that, and nothing here substitutes for it. |
| `AccessService` - the PIN gate | Staff being able to *see* money and not write it is an interaction with the window. Step 5 of `TESTING.md`. |
| `BackupService`, `GoogleDriveService` | A check that backs up or restores would have to touch `Documents\ProBooks` and the network, and a test that can move a real shop's database is worse than no test. Steps 6 and 6b: back up, then read a backup back. |
| `LicenseService` | Activation, the key and the remote call. No shop figure is worked out there. |
| `ExportService` | `WriteCsv` and `ProfitWorkbook` join rows the *pages* hand them; the figures are asserted where they are computed. Compare one exported total against the page it came from. |
| `UpdateService` - the fetch, the build, the relaunch | The rules it decides by are checked by name in `Updates`, and they are the whole of the judgement. Running git, building the app and starting a new one cannot be done from a check: it would take the machine it is verifying. Step 8 of `TESTING.md` is the one that closes that gap, and the two things it insists on - the books backed up first, and the data folder never named by the update script - are asserted in the checks themselves. |
| `PrintService.OpenHtml`, `WhatsApp` | They hand a file or a link to Windows. A check that opens a browser hangs on a machine with no browser, so the message and the number are checked *before* the launch, and the launch itself is step `4n`. |
| `PrintService.ShareBalance`, `CashBookService.PostExpense` / `SyncExpense` / `Remove*` / `PostRefunds` / `PostSupplierReceipt` | Called inside the paths above, and asserted through them - the till line after an expense edit, the receipt line after a deleted payment - rather than twice. |
| `InventoryService.GetContainerAsync`, `LedgerService.ListCustomersAsync` | Read-only fetches. The figures they carry are checked at the pages built from them. |

## Rules for a check worth adding

1. Drive the app's own services. A row written straight into the table proves the table, not the app.
2. A wrapped message needs its `+`: writing `Eq("part one "` and then `"part two", a, b)` on the next line is
   C, not C#. Say it loudly - that slip cost this file four errors, and those four hid five more, because a
   file with a parse error gets no report on anything it cannot bind.
3. A check belongs in the method that owns the fixtures it reads. Anything that speaks of the container or the
   customer built at the top of `Flows` has to live inside `Flows`: the exactness sweep had drifted out to
   class level, where those names do not exist, and it had therefore never been compiled at all.
4. **Hand-derive the expected figure in rupees and paisa.** If you cannot work it out on paper, it is not a
   check, it is a screenshot - and it will happily agree with a bug.
5. Compare a page's figure against the rows the page is built from. Two derivations, one answer, and any
   drift between them is the bug you were looking for.
6. Never compare a rounded figure to an unrounded one. Round both sides, or read both from the same `Money`
   call. This has bitten this book before: two screens, one figure, a paisa apart.
7. Prefer *relative* assertions where the fixture has history - the difference before and after the change -
   so one group of figures cannot hide in another's total.
8. No hardcoded years. Derive from `DateTime.Today`, or the check starts failing on its own timetable.
9. A refused write is a check: use `Throws<T>`. Guards are money rules with the sign left off.
10. Never launch a browser, a printer, the clipboard or a file from a check.
11. A detail string is evaluated whether the check passes or fails, so it must not throw: no `.Single()`, no
   parse, no `.First()` on something that may be empty.
12. If a check writes something the later checks do not expect, delete it in the same block. `Reconciliation`
    books a throwaway container and takes it away again so the sections after it read the fixture they expect.
13. **Run it.** A check nobody has run is a rumour about a check.

## Before a build, a thirty-second gate

    python3 tools/check_quotes.py

`tools/check_quotes.py` walks every `.cs` file the way the compiler does - raw strings, verbatim strings,
interpolated text, char literals, comments - and reports a string that never closes, a char that swallows the
end of its line, an adjacent pair of string literals with no `+` between them, and any drift in braces or
parentheses. It exists because two separate builds were lost to exactly those: a `\"` that a patch script ate
into a plain `\"`, and a wrapped message written the way C writes it. It is not a compiler and does not
pretend to be - it will not know that a figure is wrong - but it costs nothing, and it catches the faults that
hide every other fault in the file, because a file with a syntax error gets no report on anything the compiler
could not bind.

It reads the pages as well, for the one thing the compiler is slow to explain: a `Margin`, `Padding` or
`BorderThickness` that is not one, two or four numbers. `Margin="0,0,16"` - three, after a retyped `0,0,0,16`
lost a zero - stops a build with AVLN2005 on a line that says nothing about the cause. `check.bat` runs this
gate before it builds, so the answer arrives in a second instead of after a restore.

## Reading a failure

`FAIL  <name>   ->   expected 4900, got 4900.0049999` is a real finding: something started rounding twice,
or not at all.

A failure in the sweep at the end of `Reconciliation` names the customer or the lot, which is where to start.
A failure in an earlier group usually means a rule was changed on purpose - in which case the *number in the
check* has to be re-derived by hand and both the app and the check updated together. Never by editing the
expectation to match the output: that is how a book stops being checked.
