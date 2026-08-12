# TMobileScraper

Simple **.exe** — T-Mobile catalog scrape karke Excel network folder mein save karta hai.

**GitHub:** [HassebUddin/TMobileScraper](https://github.com/HassebUddin/TMobileScraper)

---

## IT ke liye (3 steps)

### 1. Publish — ek folder mein exe banao

```powershell
cd TMobileScraper.Worker
dotnet publish -c Release -o C:\TMobileScraper
```

Folder mein `TMobileScraper.exe` + `appsettings.json` hoga.

### 2. appsettings.json edit karo

```json
{
  "ConnectionStrings": {
    "TechnoDevDbConnection": "YOUR_MYSQL_CONNECTION"
  },
  "Scraping": {
    "Headless": true,
    "TimeoutSeconds": 120,
    "OutputFolder": "\\\\192.168.1.3\\Bot_Data_IT"
  }
}
```

### 3. Task Scheduler — roz raat 12 baje

1. **Task Scheduler** kholo → Create Task
2. **Trigger:** Daily, **12:00 AM**
3. **Action:** Start a program  
   - Program: `C:\TMobileScraper\TMobileScraper.exe`
   - Start in: `C:\TMobileScraper`
4. Save

Bas. Roz exe chalegi → Excel save → band.

---

## Local test

```powershell
dotnet run --project TMobileScraper.Worker
```

---

## Server pe chahiye

- Windows + .NET 8 Runtime
- VPN (agar T-Mobile block kare)
- Network share write access
- Pehli build pe Playwright Chromium auto install hota hai

---

## GitHub push

```powershell
cd D:\Active8\TMobileScraper
git init
git add .
git commit -m "TMobile catalog scraper exe for Task Scheduler"
git branch -M main
git remote add origin https://github.com/HassebUddin/TMobileScraper.git
git push -u origin main
```

Pehle GitHub pe **HassebUddin** profile se empty repo `TMobileScraper` bana lena.
