# CargoKhata

Native **desktop** app (Windows and Mac) for importers who bring containers from China.

Each container is its own stock store. Sales must name the container. Customers have a khata. SQLite lives in your Documents folder.

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

A **desktop window** opens. No browser, no localhost.

First launch asks for a **license**. You (the seller) click **I am the seller**, type the seller PIN and the customer’s business name. That name is stamped on the shop for life.

To pause a copy that has not paid, edit `license-status.json` **in this GitHub repo** and push:

```json
"shops": {
  "CK-XXXXXX": { "active": false, "message": "Payment overdue. Contact CargoKhata." }
}
```

The shop’s running app fetches that file from GitHub about once a minute. **They do not git pull and they do not need to relaunch.** Set `"active": true` (or delete the id) to unlock them the same way. The license id is on **Settings**.

To print a key from a terminal:

```bash
dotnet run --project src/ContainerManagement -- --issue "Ahmad Traders"
```

Database file (backup this):

- Windows: `Documents\CargoKhata\cargokhata.db`
- Mac: `~/Documents/CargoKhata/cargokhata.db`

Google Drive (optional): on **Backup**, link your Drive folder or sign in. Copies go to `CargoKhata/backups_from_31Aug_to_10Sep` (30 files per folder, then a new dated folder).

First launch seeds demo containers, customers, credit sales, and payments.

If you already ran an older web build, this desktop app uses a **new** database path. Demo data will appear again. That is expected.

## Publish an installer-style exe / Mac binary

Windows x64:

```bash
dotnet publish src/ContainerManagement -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win
```

Run `publish/win/CargoKhata.exe`.

Mac Apple Silicon:

```bash
dotnet publish src/ContainerManagement -c Release -r osx-arm64 --self-contained true -o publish/mac
```

Mac Intel:

```bash
dotnet publish src/ContainerManagement -c Release -r osx-x64 --self-contained true -o publish/mac
```

Then run `publish/mac/CargoKhata`.

## What to click

| Need | Where |
| --- | --- |
| New container + goods | **Containers** → fill title → Create → add goods / expenses |
| Sell (must pick container) | **New Sale** |
| Grand inventory | **Grand Inventory** |
| Customer khata | **Customers** → double-click a name |
| Collect money | Customer screen → **Record payment** |
| Uncollected credit | **Money in Market** |
| Profit per container + total | **Profit** |
| Local + Google Drive copies | **Backup** |
| Invoice / khata print, PIN, wipe demo | **Settings** + sale / customer screens |

Profit = sales revenue − cost of sold goods − expenses on that container. Unsold stock is valuation, not profit.
