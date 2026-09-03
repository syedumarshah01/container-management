# ProBooks

Native **desktop** app (Windows and Mac) for importers who bring containers from China.

Each container is its own stock store. Sales must name the container. Customers have a ledger. SQLite lives in your Documents folder.

## Stack

- **C# / .NET 8**
- **Avalonia 11** — real desktop window, not a browser
- **EF Core + SQLite**

## Run

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) for your OS, then in this folder:

**Windows:** double-click `run.bat`  
**Mac:** in Terminal:

```bash
chmod +x run.sh
./run.sh
```

Or:

```bash
dotnet run --project src/ContainerManagement
```

A **desktop window** opens. No browser, no localhost, no command-line window.

First launch asks for a **license**. You (the seller) click **I am the seller**, type the seller PIN and the customer’s business name. That name is stamped on the shop for life.

To pause a copy that has not paid, edit `license-status.json` **in this GitHub repo** and push:

```json
"shops": {
  "CK-XXXXXX": { "active": false, "message": "Payment overdue. Contact ProBooks." }
}
```

The shop’s running app fetches that file from GitHub about twice a day. **They do not git pull and they do not need to relaunch.** Set `"active": true` (or delete the id) to unlock them the same way. The license id is on **Settings**. The paused shop can also tap **Check again**.

To print a key from a terminal:

```bash
dotnet run --project src/ContainerManagement -- --issue "Ahmad Traders"
```

Database file (backup this):

- Windows: `Documents\ProBooks\probooks.db` (existing shops keep using `Documents\CargoKhata` if that folder is already there)
- Mac: `~/Documents/ProBooks/probooks.db`

Google Drive (optional): on **Backup**, link your Drive folder or sign in. Copies go to `ProBooks/backups_from_31Aug_to_10Sep` (30 files per folder, then a new dated folder).

First launch seeds demo containers, customers, credit sales, and payments.

If you already ran an older web build, this desktop app uses a **new** database path. Demo data will appear again. That is expected.

## Publish for a customer PC

On your Windows PC, double-click `publish.bat`. Copy the **whole** `publish/win` folder to the shop (USB or Drive). Double-click `ProBooks.exe` there. The shop does not need Git or the .NET SDK.

Do not copy only the exe — the other files in that folder are the Windows runtime.

Mac Apple Silicon:

```bash
dotnet publish src/ContainerManagement -c Release -r osx-arm64 --self-contained true -p:PublishReadyToRun=true -o publish/mac
```

Mac Intel:

```bash
dotnet publish src/ContainerManagement -c Release -r osx-x64 --self-contained true -p:PublishReadyToRun=true -o publish/mac
```

Then run `publish/mac/ProBooks`.

## What to click

| Need | Where |
| --- | --- |
| Plan a China order before buying | **Buy plan** → New plan → add rows |
| New container + items | **Containers** → fill title → Create → add items / expenses |
| Sell (must pick container) | **New Sale** |
| Grand inventory | **Grand Inventory** |
| Customer ledger | **Customers** → double-click a name |
| Collect money | Customer screen → **Record payment** |
| Uncollected credit | **Money in Market** |
| Profit per container + total | **Profit** |
| Local + Google Drive copies | **Backup** |
| Invoice / ledger print, PIN, wipe demo | **Settings** + sale / customer screens |

Profit = sales revenue − cost of sold items − expenses on that container. Unsold stock is valuation, not profit.

## Buy plan (the paper before the purchase)

This is the sheet you write before ordering: every item you mean to buy, with its quantity, its
cost per piece **in yen**, the weight per piece, and the price you plan to sell at. The page does
the arithmetic of the paper list:

| Box | How it is worked out |
| --- | --- |
| Total cost in yen | quantity × cost per piece, summed over the rows |
| Cost in rupees | the yen total × your **Rs per 1 yen** rate (one rate per plan) |
| Total weight | weight per piece × quantity, summed |
| If everything sells | quantity × sale price, summed |
| Going in | cost in rupees **+ the one total-expense figure** (freight, customs, clearing, labour) |
| Profit | if everything sells − going in |

A row's own profit column is its selling total minus the goods cost only — the expense figure is
for the whole lot, so it is taken off once, at the plan level. Nothing on this page moves stock or
touches a customer ledger: it is a plan, not a purchase. When the container actually lands, add it
on **Containers** as you do today.

Plans are saved (and can be duplicated for the next order). **Duplicate** is the quick way to start
the next month from last month's list.
